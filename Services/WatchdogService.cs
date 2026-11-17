using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using WireFox.Models;

namespace WireFox.Services
{
    public class WatchdogService : IDisposable
    {
        private static readonly Lazy<WatchdogService> _instance = new(() => new WatchdogService());
        public static WatchdogService Instance => _instance.Value;

        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly System.Timers.Timer _periodicTimer;
        private readonly object _lock = new();

        private TunnelStatus _status = TunnelStatus.Disconnected;
        private WireGuardTunnelInfo _currentTunnelInfo = new();
        private DateTime? _latestHandshakeUtc;
        private DateTime? _sessionExpiresUtc;
        private DateTime? _tunnelStartedUtc;
        private bool _blockedWarningSent;
        private bool _warningSentForCurrentSession;
        private bool _isManuallyDisabled;
        private bool _isStarted;
        private int _consecutiveFailures = 0;
        private readonly List<DateTime> _restartTimestamps = new();
        private readonly SemaphoreSlim _watchdogLock = new(1, 1);
        private int _isPeriodicRunning;
        private long _lastTxBytes = 0;
        private long _lastRxBytes = 0;

        public event Action<TunnelStatus>? StatusChanged;
        public event Action<DateTime?>? HandshakeUpdated;
        public event Action<WireGuardTunnelInfo>? TunnelDetailsUpdated;
        public event Action<TimeSpan?>? SessionTimeRemainingUpdated;

        public TunnelStatus Status => _status;
        public WireGuardTunnelInfo CurrentTunnelInfo => _currentTunnelInfo;
        public DateTime? LatestHandshakeUtc => _latestHandshakeUtc;
        public DateTime? SessionExpiresUtc => _sessionExpiresUtc;
        public bool IsManuallyDisabled => _isManuallyDisabled;

        public WatchdogService()
        {
            _periodicTimer = new System.Timers.Timer(8000); // 8s intervals
            _periodicTimer.Elapsed += async (s, e) => await OnPeriodicCheckAsync();
            _periodicTimer.AutoReset = true;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isStarted) return;
                _isStarted = true;

                NetworkMonitorService.Instance.NetworkSettled += OnNetworkSettled;
                NotificationService.Instance.ActionTriggered += OnNotificationAction;
                ConfigManager.Instance.ConfigChanged += OnConfigChanged;

                NetworkMonitorService.Instance.Start();
                _periodicTimer.Start();

                LoggingService.Instance.Info("WatchdogService", "Watchdog engine started");

                // Initial run
                Task.Run(async () => await RefreshStatusAsync());
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isStarted) return;
                _isStarted = false;

                _periodicTimer.Stop();
                NetworkMonitorService.Instance.NetworkSettled -= OnNetworkSettled;
                NotificationService.Instance.ActionTriggered -= OnNotificationAction;
                ConfigManager.Instance.ConfigChanged -= OnConfigChanged;
                NetworkMonitorService.Instance.Stop();

                LoggingService.Instance.Info("WatchdogService", "Watchdog engine stopped");
            }
        }

        private async void OnNetworkSettled(NetworkState state)
        {
            try
            {
                await _watchdogLock.WaitAsync();
                try
                {
                var config = ConfigManager.Instance.Config;
                var cli = WireGuardCliService.Instance;

                if (!config.InitialSetupCompleted)
                {
                    LoggingService.Instance.Debug("WatchdogService", "Initial setup not completed; skipping automatic tunnel actions.");
                    SetStatus(TunnelStatus.Disconnected);
                    return;
                }

                if (!cli.IsInstalled)
                {
                    SetStatus(TunnelStatus.Disconnected);
                    return;
                }

                string targetInterface = GetEffectiveInterfaceName(config);
                bool isRunning = cli.IsTunnelServiceRunning(targetInterface);

                LoggingService.Instance.Info("WatchdogService", 
                    $"Network settled: IsTrusted={state.IsTrusted}, Interface='{targetInterface}', IsRunning={isRunning}, ManuallyDisabled={_isManuallyDisabled}");

                // 1. If connected to a Trusted network -> Bypass VPN
                if (state.IsTrusted)
                {
                    _sessionExpiresUtc = null;

                    // Guard: If already bypassed and no tunnel is active, avoid teardown churn
                    if (_status == TunnelStatus.Bypassed && !isRunning)
                    {
                        LoggingService.Instance.Debug("WatchdogService", "Network settled on trusted network: tunnel already bypassed and inactive.");
                        return;
                    }

                    // If no tunnel is running, transition status directly without executing process teardowns
                    if (!isRunning)
                    {
                        LoggingService.Instance.Info("WatchdogService", "Trusted network active: no tunnels running, setting status to Bypassed.");
                        SetStatus(TunnelStatus.Bypassed);
                        return;
                    }

                    LoggingService.Instance.Info("WatchdogService", $"Trusted network active: stopping active tunnel '{targetInterface}' gracefully...");
                    await cli.StopTunnelAsync(targetInterface);
                    SetStatus(TunnelStatus.Bypassed);
                    return;
                }

                // 2. If untrusted wireless network and new -> Prompt user with accurate state
                if (state.IsWireless && !string.IsNullOrEmpty(state.Ssid))
                {
                    if (!config.KnownUntrustedNetworks.Contains(state.Ssid, StringComparer.OrdinalIgnoreCase) &&
                        !config.TrustedNetworks.Contains(state.Ssid, StringComparer.OrdinalIgnoreCase))
                    {
                        NotificationService.Instance.ShowNewNetworkPrompt(state.Ssid, isRunning);
                    }
                }

                var profile = config.GetProfile(targetInterface);

                // 3. If currently in a paused or split session, maintain session
                if (_status == TunnelStatus.Paused && _sessionExpiresUtc > DateTime.UtcNow)
                {
                    return;
                }

                if (_status == TunnelStatus.SplitTunnel && _sessionExpiresUtc > DateTime.UtcNow)
                {
                    await cli.SetSplitTunnelAllowedIpsAsync(targetInterface, profile.SplitTunnelAllowedIPs);
                    return;
                }

                // 4. If user manually turned OFF the tunnel, respect their choice
                if (_isManuallyDisabled)
                {
                    LoggingService.Instance.Info("WatchdogService", "Tunnel is manually disabled by user; skipping auto-start.");
                    SetStatus(TunnelStatus.Disconnected);
                    return;
                }

                // 5. Guard against unready physical networks on untrusted networks:
                // WireGuard 1.1 requires a functioning default gateway to perform endpoint DNS resolution.
                if (string.IsNullOrEmpty(state.GatewayIp))
                {
                    LoggingService.Instance.Info("WatchdogService", 
                        $"Untrusted network '{state.ConnectionType}' has no default gateway assigned yet. Deferring tunnel start until network config settles.");
                    return;
                }

                // 6. Otherwise, ensure tunnel is active on untrusted network
                if (!isRunning)
                {
                    SetStatus(TunnelStatus.Connecting);
                    _tunnelStartedUtc = DateTime.UtcNow;
                    _blockedWarningSent = false;
                    bool started = await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
                    if (!started)
                    {
                        LoggingService.Instance.Warning("WatchdogService", $"Automatic tunnel start failed for '{targetInterface}'");
                    }
                }
                else
                {
                    // If tunnel service is running but handshake is stale after wake/reconnect, refresh it gracefully
                    var latestHandshake = await cli.GetLatestHandshakeUtcAsync(targetInterface);
                    if (latestHandshake.HasValue && (DateTime.UtcNow - latestHandshake.Value).TotalSeconds > profile.HandshakeTimeoutSeconds)
                    {
                        LoggingService.Instance.Warning("WatchdogService", 
                            $"Network settled on untrusted network but running tunnel '{targetInterface}' has stale handshake. Refreshing tunnel connection...");
                        await cli.StopTunnelAsync(targetInterface);
                        _tunnelStartedUtc = DateTime.UtcNow;
                        _consecutiveFailures = 0;
                        _blockedWarningSent = false;
                        await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
                    }
                }

                await Task.Delay(2000);
                await RefreshStatusCoreAsync();
                }
                finally
                {
                    _watchdogLock.Release();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("WatchdogService", "OnNetworkSettled error", ex);
            }
        }

        private async Task OnPeriodicCheckAsync()
        {
            if (Interlocked.CompareExchange(ref _isPeriodicRunning, 1, 0) != 0)
            {
                return;
            }

            try
            {
                await _watchdogLock.WaitAsync();
                try
                {
                    // 1. Check Session Timer (Pause or Split Tunnel)
                    if (_sessionExpiresUtc.HasValue)
                    {
                        var remaining = _sessionExpiresUtc.Value - DateTime.UtcNow;
                        if (remaining <= TimeSpan.Zero)
                        {
                            // Session expired - Revert to full tunnel
                            _sessionExpiresUtc = null;
                            _warningSentForCurrentSession = false;
                            SessionTimeRemainingUpdated?.Invoke(null);
                            await RevertToFullTunnelCoreAsync();
                        }
                        else
                        {
                            SessionTimeRemainingUpdated?.Invoke(remaining);

                            // 5-minute warning toast
                            if (remaining <= TimeSpan.FromMinutes(5) && !_warningSentForCurrentSession)
                            {
                                _warningSentForCurrentSession = true;
                                string sessionName = _status == TunnelStatus.Paused ? "Pause" : "Split-Tunnel";
                                int mins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                                NotificationService.Instance.ShowTimerExpiringWarning(sessionName, mins);
                            }
                        }
                    }

                    // 2. Check Handshake & Tunnel State
                    await RefreshStatusCoreAsync();
                    await CheckHandshakeWatchdogCoreAsync();
                }
                finally
                {
                    _watchdogLock.Release();
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("WatchdogService", "Periodic check error", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isPeriodicRunning, 0);
            }
        }

        public async Task RefreshStatusAsync()
        {
            await _watchdogLock.WaitAsync();
            try
            {
                await RefreshStatusCoreAsync();
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        private async Task RefreshStatusCoreAsync()
        {
            var config = ConfigManager.Instance.Config;
            var cli = WireGuardCliService.Instance;
            var netState = NetworkMonitorService.Instance.CurrentState;

            if (netState.IsTrusted)
            {
                SetStatus(TunnelStatus.Bypassed);
                return;
            }

            if (_status == TunnelStatus.Paused && _sessionExpiresUtc > DateTime.UtcNow)
            {
                return;
            }

            string effectiveInterface = GetEffectiveInterfaceName(config);
            bool isRunning = cli.IsTunnelServiceRunning(effectiveInterface);

            if (!isRunning)
            {
                if (_status != TunnelStatus.Paused)
                {
                    SetStatus(TunnelStatus.Disconnected);
                }
                _currentTunnelInfo = new WireGuardTunnelInfo { InterfaceName = effectiveInterface, IsActive = false };
                TunnelDetailsUpdated?.Invoke(_currentTunnelInfo);
                return;
            }

            var details = await cli.GetTunnelDetailsAsync(effectiveInterface);
            _currentTunnelInfo = details;
            _latestHandshakeUtc = details.LatestHandshakeUtc;
            
            HandshakeUpdated?.Invoke(details.LatestHandshakeUtc);
            TunnelDetailsUpdated?.Invoke(details);

            if (_status != TunnelStatus.SplitTunnel)
            {
                SetStatus(TunnelStatus.Connected);
            }
        }

        private string GetEffectiveInterfaceName(AppConfig config)
        {
            // 1. Prioritize explicitly selected or configured interface
            if (!string.IsNullOrWhiteSpace(config.WireGuardInterface) && 
                !string.Equals(config.WireGuardInterface, "wg0", StringComparison.OrdinalIgnoreCase))
            {
                return config.WireGuardInterface;
            }

            // 2. If configured is "wg0" or blank, see if an active interface exists
            var cli = WireGuardCliService.Instance;
            var active = cli.GetActiveInterfaceNames();
            if (active.Count > 0)
            {
                return active.First();
            }

            // 3. Fallback to first discovered available tunnel
            var discovered = cli.GetDiscoveredTunnelNames();
            var nonWg0 = discovered.FirstOrDefault(t => !string.Equals(t, "wg0", StringComparison.OrdinalIgnoreCase));
            if (nonWg0 != null)
            {
                return nonWg0;
            }

            return !string.IsNullOrWhiteSpace(config.WireGuardInterface) ? config.WireGuardInterface : "wg0";
        }

        private async Task<bool> CheckInternetHealthAsync()
        {
            try
            {
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var response = await _httpClient.GetAsync("http://connectivitycheck.gstatic.com/generate_204", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                try
                {
                    var dnsTask = Dns.GetHostAddressesAsync("www.msftconnecttest.com");
                    if (await Task.WhenAny(dnsTask, Task.Delay(2000)) == dnsTask)
                    {
                        var addrs = await dnsTask;
                        return addrs != null && addrs.Length > 0;
                    }
                }
                catch { }
                return false;
            }
        }

        private async Task CheckHandshakeWatchdogCoreAsync()
        {
            var config = ConfigManager.Instance.Config;
            if (!config.InitialSetupCompleted)
            {
                return;
            }

            var cli = WireGuardCliService.Instance;
            string targetInterface = GetEffectiveInterfaceName(config);

            if (!cli.IsInstalled)
            {
                return;
            }

            if (!cli.IsTunnelServiceRunning(targetInterface))
            {
                return;
            }

            // Allow initial 25-second grace period after starting
            if (_tunnelStartedUtc.HasValue && (DateTime.UtcNow - _tunnelStartedUtc.Value).TotalSeconds < 25)
            {
                return;
            }

            var handshake = await cli.GetLatestHandshakeUtcAsync(targetInterface);
            _latestHandshakeUtc = handshake;
            HandshakeUpdated?.Invoke(handshake);

            var profile = config.GetProfile(targetInterface);
            bool handshakeStale = false;
            if (handshake == null || (DateTime.UtcNow - handshake.Value).TotalSeconds > profile.HandshakeTimeoutSeconds)
            {
                handshakeStale = true;
            }

            // Flow signature check: TX rising with zero RX response
            var details = await cli.GetTunnelDetailsAsync(targetInterface);
            bool flowBlackholed = false;
            if (_lastTxBytes > 0 && details.TransferTxBytes > _lastTxBytes + 5000 && details.TransferRxBytes == _lastRxBytes)
            {
                flowBlackholed = true;
            }
            _lastTxBytes = details.TransferTxBytes;
            _lastRxBytes = details.TransferRxBytes;

            // Probe internet connectivity (DNS / HTTP 204)
            bool internetHealthy = await CheckInternetHealthAsync();

            if (internetHealthy && !handshakeStale)
            {
                _consecutiveFailures = 0;
                _blockedWarningSent = false;
                return;
            }

            // If internet probe failed or handshake is stale, check if physical router is reachable
            bool gatewayReachable = await NetworkMonitorService.Instance.IsLocalGatewayReachableAsync();
            if (!gatewayReachable)
            {
                // Physical network itself is disconnected or down - do not blame WireGuard
                LoggingService.Instance.Debug("WatchdogService", 
                    "Local default gateway is unreachable; physical network is offline. Skipping tunnel recovery.");
                return;
            }

            // Local router IS reachable, but tunnel traffic is blackholed / dead
            _consecutiveFailures++;
            LoggingService.Instance.Warning("WatchdogService", 
                $"Tunnel degradation detected on '{targetInterface}' (Failures={_consecutiveFailures}, HandshakeStale={handshakeStale}, FlowBlackholed={flowBlackholed}, InternetHealthy={internetHealthy})");

            if (_consecutiveFailures < 2)
            {
                // Need 2 consecutive failed intervals (approx 16s) to avoid reacting to temporary glitches
                return;
            }

            string action = !string.IsNullOrWhiteSpace(config.ActionOnFailure) ? config.ActionOnFailure.ToLowerInvariant() : "restart";

            switch (action)
            {
                case "pause":
                    LoggingService.Instance.Warning("WatchdogService", "ActionOnFailure='pause'. Pausing tunnel for 15 minutes.");
                    await PauseTunnelCoreAsync(TimeSpan.FromMinutes(15));
                    NotificationService.Instance.ShowNotification("VPN Paused (Traffic Blocked)", 
                        $"Tunnel '{targetInterface}' was paused for 15 minutes because network traffic was blocked.");
                    break;

                case "prompt":
                    if (!_blockedWarningSent)
                    {
                        _blockedWarningSent = true;
                        LoggingService.Instance.Warning("WatchdogService", "ActionOnFailure='prompt'. Alerting user via toast.");
                        NotificationService.Instance.ShowWatchdogBlockedPrompt(targetInterface);
                    }
                    break;

                case "restart":
                default:
                    _restartTimestamps.RemoveAll(t => DateTime.UtcNow - t > TimeSpan.FromMinutes(3));
                    if (_restartTimestamps.Count >= 2)
                    {
                        LoggingService.Instance.Warning("WatchdogService", 
                            "Circuit breaker tripped: Repeated tunnel restarts within 3 minutes. Pausing tunnel to avoid loop.");
                        await PauseTunnelCoreAsync(TimeSpan.FromMinutes(15));
                        NotificationService.Instance.ShowNotification("VPN Paused (Network Restrictive)", 
                            "WireGuard was paused for 15 minutes after repeated connection lockups.");
                        return;
                    }

                    LoggingService.Instance.Warning("WatchdogService", $"Auto-recovering zombie tunnel '{targetInterface}'...");
                    await ForceRestartTunnelCoreAsync();
                    NotificationService.Instance.ShowNotification("WireGuard Restored", 
                        "WireGuard connection was automatically restored after detecting a driver freeze.");
                    break;
            }
        }

        private async void OnNotificationAction(string action, string? param)
        {
            try
            {
            switch (action)
            {
                case "pause":
                    int pauseMins = int.TryParse(param, out int pM) ? pM : 15;
                    await PauseTunnelAsync(TimeSpan.FromMinutes(pauseMins));
                    break;

                case "restart_tunnel":
                    await RestartTunnelAsync();
                    break;

                case "extend_pause":
                    int extMins = int.TryParse(param, out int eM) ? eM : 15;
                    ExtendSession(TimeSpan.FromMinutes(extMins));
                    break;

                case "revert_full":
                    await RevertToFullTunnelAsync();
                    break;

                case "trust_ssid":
                    if (!string.IsNullOrEmpty(param))
                    {
                        ConfigManager.Instance.AddTrustedNetwork(param);
                        NotificationService.Instance.ShowNotification("Network Trusted", $"'{param}' has been added to trusted networks. VPN bypassed.");
                    }
                    break;

                case "enable_vpn":
                    await _watchdogLock.WaitAsync();
                    try
                    {
                        _isManuallyDisabled = false;
                        var config = ConfigManager.Instance.Config;
                        string target = GetEffectiveInterfaceName(config);
                        var targetProfile = config.GetProfile(target);
                        await WireGuardCliService.Instance.StartTunnelAsync(target, targetProfile.WireGuardConfigPath);
                        _tunnelStartedUtc = DateTime.UtcNow;
                        _blockedWarningSent = false;
                        await RefreshStatusCoreAsync();
                    }
                    finally
                    {
                        _watchdogLock.Release();
                    }
                    break;

                case "keep_vpn":
                    if (!string.IsNullOrEmpty(param))
                    {
                        ConfigManager.Instance.MarkNetworkAsKnownUntrusted(param);
                    }
                    break;

                case "update_now":
                    try
                    {
                        if (UpdateService.Instance.LatestRelease != null)
                        {
                            _ = UpdateService.Instance.ExecuteUpdateAsync(UpdateService.Instance.LatestRelease);
                        }
                        else
                        {
                            var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
                            if (release != null)
                            {
                                _ = UpdateService.Instance.ExecuteUpdateAsync(release);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error("WatchdogService", "Failed to run update from notification", ex);
                    }
                    break;

                case "open_settings":
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        if (System.Windows.Application.Current.MainWindow is Views.MainWindow mainWin)
                        {
                            mainWin.ShowAndRestore();
                            mainWin.NavigateToSettings();
                        }
                    });
                    break;

                case "skip_update_version":
                    if (!string.IsNullOrEmpty(param))
                    {
                        ConfigManager.Instance.Config.IgnoredUpdateVersion = param;
                        ConfigManager.Instance.SaveConfig();
                        LoggingService.Instance.Info("WatchdogService", $"User chose to skip update version: {param}");
                    }
                    break;
            }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("WatchdogService", "OnNotificationAction error", ex);
            }
        }

        private string? _lastInterface;
        private int _lastTrustedNetworksCount;
        private int _lastTrustedGatewaysCount;

        private async void OnConfigChanged(AppConfig config)
        {
            try
            {
            bool interfaceChanged = !string.Equals(_lastInterface, config.WireGuardInterface, StringComparison.OrdinalIgnoreCase);
            bool networksChanged = _lastTrustedNetworksCount != config.TrustedNetworks.Count ||
                                   _lastTrustedGatewaysCount != config.TrustedGateways.Count;

            string? prevInterface = _lastInterface;
            _lastInterface = config.WireGuardInterface;
            _lastTrustedNetworksCount = config.TrustedNetworks.Count;
            _lastTrustedGatewaysCount = config.TrustedGateways.Count;

            // If user switched tunnel in settings while old tunnel was running, switch seamlessly!
            if (interfaceChanged && !string.IsNullOrEmpty(prevInterface) && !string.IsNullOrEmpty(config.WireGuardInterface))
            {
                await _watchdogLock.WaitAsync();
                try
                {
                    var cli = WireGuardCliService.Instance;
                    if (cli.IsTunnelServiceRunning(prevInterface))
                    {
                        LoggingService.Instance.Info("WatchdogService", 
                            $"Tunnel interface switched from '{prevInterface}' to '{config.WireGuardInterface}'. Switching active service...");
                        SetStatus(TunnelStatus.Connecting);
                        await cli.StopTunnelAsync(prevInterface);
                        _tunnelStartedUtc = DateTime.UtcNow;
                        _blockedWarningSent = false;
                        var activeProfile = config.GetProfile(config.WireGuardInterface);
                        await cli.StartTunnelAsync(config.WireGuardInterface, activeProfile.WireGuardConfigPath);
                        await RefreshStatusCoreAsync();
                        return;
                    }
                }
                finally
                {
                    _watchdogLock.Release();
                }
            }

            // Always trigger re-evaluation of networks to handle deletions and modifications
            NetworkMonitorService.Instance.TriggerDebouncedEvaluation(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("WatchdogService", "OnConfigChanged error", ex);
            }
        }

        public async Task PauseTunnelAsync(TimeSpan duration)
        {
            await _watchdogLock.WaitAsync();
            try
            {
                await PauseTunnelCoreAsync(duration);
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        private async Task PauseTunnelCoreAsync(TimeSpan duration)
        {
            var config = ConfigManager.Instance.Config;
            var cli = WireGuardCliService.Instance;
            string targetInterface = GetEffectiveInterfaceName(config);

            _sessionExpiresUtc = DateTime.UtcNow.Add(duration);
            _warningSentForCurrentSession = false;
            await cli.StopTunnelAsync(targetInterface);
            SetStatus(TunnelStatus.Paused);
            SessionTimeRemainingUpdated?.Invoke(duration);
        }

        public void ExtendSession(TimeSpan extraDuration)
        {
            if (_sessionExpiresUtc.HasValue && _sessionExpiresUtc.Value > DateTime.UtcNow)
            {
                _sessionExpiresUtc = _sessionExpiresUtc.Value.Add(extraDuration);
            }
            else
            {
                _sessionExpiresUtc = DateTime.UtcNow.Add(extraDuration);
            }
            _warningSentForCurrentSession = false;
            SessionTimeRemainingUpdated?.Invoke(_sessionExpiresUtc.Value - DateTime.UtcNow);
        }

        public async Task EnableSplitTunnelAsync(TimeSpan duration)
        {
            await _watchdogLock.WaitAsync();
            try
            {
                var config = ConfigManager.Instance.Config;
                var cli = WireGuardCliService.Instance;
                string targetInterface = GetEffectiveInterfaceName(config);
                var profile = config.GetProfile(targetInterface);

                _sessionExpiresUtc = DateTime.UtcNow.Add(duration);
                _warningSentForCurrentSession = false;

                if (!cli.IsTunnelServiceRunning(targetInterface))
                {
                    _tunnelStartedUtc = DateTime.UtcNow;
                    _blockedWarningSent = false;
                    await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
                }

                await cli.SetSplitTunnelAllowedIpsAsync(targetInterface, profile.SplitTunnelAllowedIPs);
                SetStatus(TunnelStatus.SplitTunnel);
                SessionTimeRemainingUpdated?.Invoke(duration);
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        public async Task RevertToFullTunnelAsync()
        {
            await _watchdogLock.WaitAsync();
            try
            {
                await RevertToFullTunnelCoreAsync();
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        private async Task RevertToFullTunnelCoreAsync()
        {
            var config = ConfigManager.Instance.Config;
            var cli = WireGuardCliService.Instance;
            string targetInterface = GetEffectiveInterfaceName(config);
            var profile = config.GetProfile(targetInterface);

            _sessionExpiresUtc = null;
            _warningSentForCurrentSession = false;
            SessionTimeRemainingUpdated?.Invoke(null);

            if (!cli.IsTunnelServiceRunning(targetInterface))
            {
                _tunnelStartedUtc = DateTime.UtcNow;
                _blockedWarningSent = false;
                await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
            }
            else
            {
                await cli.SetSplitTunnelAllowedIpsAsync(targetInterface, profile.FullTunnelAllowedIPs);
            }

            SetStatus(TunnelStatus.Connected);
        }

        public async Task RestartTunnelAsync()
        {
            await ForceRestartTunnelAsync();
        }

        public async Task ForceRestartTunnelAsync()
        {
            await _watchdogLock.WaitAsync();
            try
            {
                await ForceRestartTunnelCoreAsync();
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        private async Task ForceRestartTunnelCoreAsync()
        {
            var config = ConfigManager.Instance.Config;
            var cli = WireGuardCliService.Instance;
            string targetInterface = GetEffectiveInterfaceName(config);
            var profile = config.GetProfile(targetInterface);

            LoggingService.Instance.Warning("WatchdogService", $"Restarting tunnel '{targetInterface}' gracefully...");
            SetStatus(TunnelStatus.Connecting);

            // Attempt graceful restart first; only escalate to nuclear wipeout if graceful stop fails
            bool stopped = await cli.StopTunnelAsync(targetInterface);
            if (!stopped)
            {
                LoggingService.Instance.Warning("WatchdogService", $"Graceful stop failed for '{targetInterface}'. Escalating to full reset...");
                await cli.StopAllTunnelsAsync(true);
                await Task.Delay(1000);

                // Relaunch the WireGuard UI first so the manager is ready
                if (!string.IsNullOrEmpty(cli.WireGuardExePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = cli.WireGuardExePath,
                            UseShellExecute = true
                        });
                        await Task.Delay(1000); // Give the UI a second to initialize
                    }
                    catch { }
                }
            }
            else
            {
                await Task.Delay(500);
            }

            _tunnelStartedUtc = DateTime.UtcNow;
            _consecutiveFailures = 0;
            _blockedWarningSent = false;
            _restartTimestamps.Add(DateTime.UtcNow);

            await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
            await Task.Delay(2000);
            await RefreshStatusCoreAsync();
        }

        public async Task KillAllWireGuardAsync()
        {
            await _watchdogLock.WaitAsync();
            try
            {
                var cli = WireGuardCliService.Instance;
                LoggingService.Instance.Warning("WatchdogService", "KillAllWireGuardAsync invoked: force-stopping all tunnels!");
                _isManuallyDisabled = true;
                SetStatus(TunnelStatus.Disconnected);
                await cli.StopAllTunnelsAsync(true);
                await RefreshStatusCoreAsync();
                NotificationService.Instance.ShowNotification("WireGuard Force Stopped", 
                    "All WireGuard tunnels and kernel hooks were forcefully stopped. Normal network routing restored.");
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        public async Task ToggleTunnelManualAsync()
        {
            await _watchdogLock.WaitAsync();
            try
            {
                var config = ConfigManager.Instance.Config;
                var cli = WireGuardCliService.Instance;
                string targetInterface = GetEffectiveInterfaceName(config);
                var profile = config.GetProfile(targetInterface);

                if (cli.IsTunnelServiceRunning(targetInterface))
                {
                    // User explicitly turns OFF tunnel
                    _isManuallyDisabled = true;
                    LoggingService.Instance.Info("WatchdogService", $"User manually toggled OFF tunnel '{targetInterface}'");
                    SetStatus(TunnelStatus.Disconnected);
                    await cli.StopTunnelAsync(targetInterface);
                    await RefreshStatusCoreAsync();
                }
                else
                {
                    // User explicitly turns ON tunnel
                    _isManuallyDisabled = false;
                    LoggingService.Instance.Info("WatchdogService", $"User manually toggled ON tunnel '{targetInterface}'");
                    SetStatus(TunnelStatus.Connecting);

                    string? confPath = cli.ResolveConfigPath(targetInterface, profile.WireGuardConfigPath);
                    if (string.IsNullOrEmpty(confPath) || !System.IO.File.Exists(confPath))
                    {
                        LoggingService.Instance.Error("WatchdogService", 
                            $"No configuration file (.conf) found for '{targetInterface}'. Please choose one in Settings.");
                        NotificationService.Instance.ShowNotification("Configuration Missing", 
                            $"Could not find a .conf file for '{targetInterface}'. Please select one in Settings.");
                        SetStatus(TunnelStatus.Disconnected);
                        return;
                    }

                    _tunnelStartedUtc = DateTime.UtcNow;
                    _blockedWarningSent = false;
                    bool started = await cli.StartTunnelAsync(targetInterface, confPath);
                    if (!started)
                    {
                        SetStatus(TunnelStatus.Disconnected);
                    }
                    else
                    {
                        await RefreshStatusCoreAsync();
                    }
                }
            }
            finally
            {
                _watchdogLock.Release();
            }
        }

        public void ResetManualOverride()
        {
            _isManuallyDisabled = false;
        }

        private void SetStatus(TunnelStatus newStatus)
        {
            if (_status != newStatus)
            {
                LoggingService.Instance.Info("WatchdogService", $"Status transition: {_status} -> {newStatus}");
                _status = newStatus;
                StatusChanged?.Invoke(_status);
            }
        }

        public void Dispose()
        {
            Stop();
            _periodicTimer.Dispose();
        }
    }
}

