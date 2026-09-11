using System;
using System.Collections.Generic;
using System.Windows.Input;
using WireFox.Models;
using WireFox.Services;
using Wpf.Ui.Controls;

namespace WireFox.ViewModels
{
    public class StatusViewModel : ViewModelBase
    {
        private string _statusText = "Disconnected";
        private SymbolRegular _statusSymbol = SymbolRegular.ShieldDismiss24;
        private string _currentNetworkText = "Checking...";
        private string _gatewayInfoText = string.Empty;
        private string _handshakeAgeText = "None";
        private string _sessionCountdownText = string.Empty;
        private string _activeTunnelName = "wg0";
        private string _transferStatsText = "0 B sent, 0 B received";
        private string _endpointText = string.Empty;
        private bool _isTunnelActive;
        private bool _canTrustCurrentNetwork;
        private bool _canUntrustCurrentNetwork;
        private bool _isSessionActive;
        private bool _isMacMismatch;
        private string _macMismatchMessage = string.Empty;
        private bool _isBusy;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public SymbolRegular StatusSymbol
        {
            get => _statusSymbol;
            set => SetProperty(ref _statusSymbol, value);
        }

        public string CurrentNetworkText
        {
            get => _currentNetworkText;
            set => SetProperty(ref _currentNetworkText, value);
        }

        public string GatewayInfoText
        {
            get => _gatewayInfoText;
            set => SetProperty(ref _gatewayInfoText, value);
        }

        public string HandshakeAgeText
        {
            get => _handshakeAgeText;
            set => SetProperty(ref _handshakeAgeText, value);
        }

        public string SessionCountdownText
        {
            get => _sessionCountdownText;
            set => SetProperty(ref _sessionCountdownText, value);
        }

        public string ActiveTunnelName
        {
            get => _activeTunnelName;
            set => SetProperty(ref _activeTunnelName, value);
        }

        public string TransferStatsText
        {
            get => _transferStatsText;
            set => SetProperty(ref _transferStatsText, value);
        }

        public string EndpointText
        {
            get => _endpointText;
            set => SetProperty(ref _endpointText, value);
        }

        public bool IsTunnelActive
        {
            get => _isTunnelActive;
            set => SetProperty(ref _isTunnelActive, value);
        }

        public bool CanTrustCurrentNetwork
        {
            get => _canTrustCurrentNetwork;
            set => SetProperty(ref _canTrustCurrentNetwork, value);
        }

        public bool CanUntrustCurrentNetwork
        {
            get => _canUntrustCurrentNetwork;
            set => SetProperty(ref _canUntrustCurrentNetwork, value);
        }

        public bool IsSessionActive
        {
            get => _isSessionActive;
            set => SetProperty(ref _isSessionActive, value);
        }

        public bool IsMacMismatch
        {
            get => _isMacMismatch;
            set => SetProperty(ref _isMacMismatch, value);
        }

        public string MacMismatchMessage
        {
            get => _macMismatchMessage;
            set => SetProperty(ref _macMismatchMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set => SetProperty(ref _isBusy, value);
        }

        public ICommand ToggleTunnelCommand { get; }
        public ICommand TrustCurrentNetworkCommand { get; }
        public ICommand UntrustCurrentNetworkCommand { get; }
        public ICommand UpdateGatewayMacCommand { get; }
        public ICommand Pause15Command { get; }
        public ICommand SplitTunnel15Command { get; }
        public ICommand RevertFullCommand { get; }

        public StatusViewModel()
        {
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            ToggleTunnelCommand = new RelayCommand(OnToggleTunnelClicked);
            TrustCurrentNetworkCommand = new RelayCommand(OnTrustCurrentNetwork);
            UntrustCurrentNetworkCommand = new RelayCommand(OnUntrustCurrentNetwork);
            UpdateGatewayMacCommand = new RelayCommand(OnUpdateGatewayMac);
            Pause15Command = new RelayCommand(async () => await WatchdogService.Instance.PauseTunnelAsync(TimeSpan.FromMinutes(15)));
            SplitTunnel15Command = new RelayCommand(async () => await WatchdogService.Instance.EnableSplitTunnelAsync(TimeSpan.FromMinutes(15)));
            RevertFullCommand = new RelayCommand(async () => await WatchdogService.Instance.RevertToFullTunnelAsync());

            WatchdogService.Instance.StatusChanged += OnStatusChanged;
            WatchdogService.Instance.HandshakeUpdated += OnHandshakeUpdated;
            WatchdogService.Instance.TunnelDetailsUpdated += OnTunnelDetailsUpdated;
            WatchdogService.Instance.SessionTimeRemainingUpdated += OnSessionTimerUpdated;
            NetworkMonitorService.Instance.NetworkSettled += OnNetworkSettled;

            // Initial UI state
            ActiveTunnelName = ConfigManager.Instance.Config.WireGuardInterface;
            UpdateStatus(WatchdogService.Instance.Status);
            UpdateNetwork(NetworkMonitorService.Instance.CurrentState);
            OnHandshakeUpdated(WatchdogService.Instance.LatestHandshakeUtc);
        }

        private async void OnToggleTunnelClicked()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                await WatchdogService.Instance.ToggleTunnelManualAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnStatusChanged(TunnelStatus status)
        {
            _dispatcher.Invoke(() => UpdateStatus(status));
        }

        private void UpdateStatus(TunnelStatus status)
        {
            switch (status)
            {
                case TunnelStatus.Connected:
                    StatusText = "Connected";
                    StatusSymbol = SymbolRegular.ShieldCheckmark24;
                    _isTunnelActive = true;
                    IsSessionActive = false;
                    break;
                case TunnelStatus.Connecting:
                    StatusText = "Connecting...";
                    StatusSymbol = SymbolRegular.ArrowSync24;
                    _isTunnelActive = false;
                    IsSessionActive = false;
                    break;
                case TunnelStatus.Paused:
                    StatusText = "Paused";
                    StatusSymbol = SymbolRegular.Pause24;
                    _isTunnelActive = false;
                    IsSessionActive = true;
                    break;
                case TunnelStatus.SplitTunnel:
                    StatusText = "Split-Tunnel (LAN Only)";
                    StatusSymbol = SymbolRegular.ArrowSwap24;
                    _isTunnelActive = true;
                    IsSessionActive = true;
                    break;
                case TunnelStatus.Bypassed:
                    StatusText = "Bypassed (Trusted Network)";
                    StatusSymbol = SymbolRegular.ShieldDismiss24;
                    _isTunnelActive = false;
                    IsSessionActive = false;
                    break;
                case TunnelStatus.Disconnected:
                default:
                    StatusText = "Disconnected";
                    StatusSymbol = SymbolRegular.ShieldDismiss24;
                    _isTunnelActive = false;
                    IsSessionActive = false;
                    break;
            }
            OnPropertyChanged(nameof(IsTunnelActive));
        }

        private void OnNetworkSettled(NetworkState state)
        {
            _dispatcher.Invoke(() => UpdateNetwork(state));
        }

        private void UpdateNetwork(NetworkState state)
        {
            if (!state.IsConnected)
            {
                CurrentNetworkText = "No Network Connection";
                GatewayInfoText = string.Empty;
                CanTrustCurrentNetwork = false;
                IsMacMismatch = false;
                return;
            }

            if (state.IsWireless && !string.IsNullOrEmpty(state.Ssid))
            {
                CurrentNetworkText = state.IsTrusted ? $"{state.Ssid} (Trusted)" : $"{state.Ssid} (Untrusted)";
            }
            else if (!string.IsNullOrEmpty(state.GatewayIp))
            {
                CurrentNetworkText = state.IsTrusted ? $"{state.ConnectionType}: {state.GatewayIp} (Trusted)" : $"{state.ConnectionType}: {state.GatewayIp} (Untrusted)";
            }
            else
            {
                CurrentNetworkText = state.IsTrusted ? $"{state.ConnectionType} (Trusted)" : $"{state.ConnectionType} (Untrusted)";
            }

            var parts = new List<string>();
            if (!string.IsNullOrEmpty(state.GatewayIp))
            {
                string gwPart = $"Gateway: {state.GatewayIp}";
                if (!string.IsNullOrEmpty(state.GatewayMac))
                {
                    gwPart += $" [{state.GatewayMac}]";
                }
                parts.Add(gwPart);
            }
            if (!string.IsNullOrEmpty(state.Bssid))
            {
                parts.Add($"AP: [{state.Bssid}]");
            }
            if (!string.IsNullOrEmpty(state.DnsServerName))
            {
                parts.Add($"DNS: {state.DnsServerName}");
            }
            else if (!string.IsNullOrEmpty(state.DnsSuffix))
            {
                parts.Add($"Domain: {state.DnsSuffix}");
            }
            GatewayInfoText = string.Join("  \u2022  ", parts);

            CanTrustCurrentNetwork = !state.IsTrusted && (!string.IsNullOrEmpty(state.Ssid) || !string.IsNullOrEmpty(state.GatewayIp));
            CanUntrustCurrentNetwork = state.IsTrusted && (!string.IsNullOrEmpty(state.Ssid) || !string.IsNullOrEmpty(state.GatewayIp));

            IsMacMismatch = state.IsGatewayMacMismatch;
            if (state.IsGatewayMacMismatch)
            {
                MacMismatchMessage = $"Gateway MAC changed! Registered [{state.RegisteredGatewayMac}] vs Detected [{state.GatewayMac}]. VPN kept active for protection.";
            }
            else
            {
                MacMismatchMessage = string.Empty;
            }
        }

        private void OnTunnelDetailsUpdated(WireGuardTunnelInfo info)
        {
            _dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(info.InterfaceName))
                {
                    ActiveTunnelName = info.InterfaceName;
                }
                TransferStatsText = info.FormattedTransfer;
                EndpointText = !string.IsNullOrEmpty(info.Endpoint) ? $"Endpoint: {info.Endpoint}" : string.Empty;
                OnHandshakeUpdated(info.LatestHandshakeUtc);
            });
        }

        private void OnHandshakeUpdated(DateTime? handshakeUtc)
        {
            _dispatcher.Invoke(() =>
            {
                if (!handshakeUtc.HasValue)
                {
                    HandshakeAgeText = "No handshake yet";
                    return;
                }

                var elapsed = DateTime.UtcNow - handshakeUtc.Value;
                if (elapsed.TotalSeconds < 5) HandshakeAgeText = "Just now";
                else if (elapsed.TotalSeconds < 60) HandshakeAgeText = $"{(int)elapsed.TotalSeconds} seconds ago";
                else if (elapsed.TotalMinutes < 60) HandshakeAgeText = $"{(int)elapsed.TotalMinutes} minutes ago";
                else HandshakeAgeText = handshakeUtc.Value.ToLocalTime().ToString("g");
            });
        }

        private void OnSessionTimerUpdated(TimeSpan? remaining)
        {
            _dispatcher.Invoke(() =>
            {
                if (!remaining.HasValue || remaining.Value <= TimeSpan.Zero)
                {
                    SessionCountdownText = string.Empty;
                    IsSessionActive = false;
                }
                else
                {
                    SessionCountdownText = $"Expires in {remaining.Value.Minutes:D2}:{remaining.Value.Seconds:D2}";
                    IsSessionActive = true;
                }
            });
        }

        private void OnTrustCurrentNetwork()
        {
            var netState = NetworkMonitorService.Instance.CurrentState;
            if (netState.IsWireless && !string.IsNullOrEmpty(netState.Ssid))
            {
                ConfigManager.Instance.AddTrustedNetwork(netState.Ssid);
            }

            if (!string.IsNullOrEmpty(netState.GatewayIp))
            {
                var stacked = new List<string>();
                if (!string.IsNullOrWhiteSpace(netState.Ssid))
                {
                    stacked.Add(netState.Ssid);
                }
                else if (!string.IsNullOrWhiteSpace(netState.DnsSuffix))
                {
                    stacked.Add(netState.DnsSuffix);
                }

                var gw = new TrustedGateway
                {
                    Name = !string.IsNullOrWhiteSpace(netState.Ssid) ? netState.Ssid : (netState.DnsServerName ?? $"Gateway {netState.GatewayIp}"),
                    IpAddress = netState.GatewayIp,
                    MacAddress = netState.GatewayMac ?? string.Empty,
                    StackedNames = stacked
                };
                ConfigManager.Instance.AddOrUpdateTrustedGateway(gw);
            }

            NetworkMonitorService.Instance.TriggerDebouncedEvaluation(TimeSpan.FromMilliseconds(200));
        }

        private void OnUntrustCurrentNetwork()
        {
            var netState = NetworkMonitorService.Instance.CurrentState;
            LoggingService.Instance.Info("Status", $"User untrusting current network SSID='{netState.Ssid}', Gateway='{netState.GatewayIp}'");
            WatchdogService.Instance.ResetManualOverride();
            ConfigManager.Instance.UntrustNetwork(netState.Ssid, netState.GatewayIp, netState.GatewayMac);
            NetworkMonitorService.Instance.TriggerDebouncedEvaluation(TimeSpan.FromMilliseconds(50));
        }

        private void OnUpdateGatewayMac()
        {
            var netState = NetworkMonitorService.Instance.CurrentState;
            if (!string.IsNullOrEmpty(netState.GatewayIp) && !string.IsNullOrEmpty(netState.GatewayMac) && !string.IsNullOrEmpty(netState.RegisteredGatewayMac))
            {
                ConfigManager.Instance.UpdateGatewayMac(netState.GatewayIp, netState.RegisteredGatewayMac, netState.GatewayMac);
                NetworkMonitorService.Instance.TriggerDebouncedEvaluation(TimeSpan.FromMilliseconds(200));
            }
        }
    }
}
