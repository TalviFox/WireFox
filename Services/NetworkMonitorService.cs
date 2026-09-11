using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WireFox.Models;

namespace WireFox.Services
{
    public class NetworkMonitorService : IDisposable
    {
        private static readonly Lazy<NetworkMonitorService> _instance = new(() => new NetworkMonitorService());
        public static NetworkMonitorService Instance => _instance.Value;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        private CancellationTokenSource? _debounceCts;
        private readonly object _lock = new();
        private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(2000);
        private NetworkState _currentState = new();
        private bool _isStarted;

        public event Action<NetworkState>? NetworkSettled;
        public NetworkState CurrentState => _currentState;

        public NetworkMonitorService()
        {
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isStarted) return;
                _isStarted = true;

                NetworkChange.NetworkAddressChanged += OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
                SystemEvents.PowerModeChanged += OnPowerModeChanged;

                LoggingService.Instance.Info("NetworkMonitor", "Network monitor started");

                // Initial trigger
                TriggerDebouncedEvaluation(TimeSpan.FromMilliseconds(500));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isStarted) return;
                _isStarted = false;

                NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
                NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;

                LoggingService.Instance.Info("NetworkMonitor", "Network monitor stopped");
            }
        }

        private void OnNetworkChanged(object? sender, EventArgs e)
        {
            LoggingService.Instance.Debug("NetworkMonitor", "Network address changed event fired");
            TriggerDebouncedEvaluation(_debounceDelay);
        }

        private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            LoggingService.Instance.Debug("NetworkMonitor", $"Network availability changed: IsAvailable={e.IsAvailable}");
            TriggerDebouncedEvaluation(_debounceDelay);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                LoggingService.Instance.Info("NetworkMonitor", "System resumed from sleep, queuing evaluation");
                TriggerDebouncedEvaluation(TimeSpan.FromSeconds(3));
            }
        }

        public void TriggerDebouncedEvaluation(TimeSpan delay)
        {
            lock (_lock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(delay, token);
                        if (token.IsCancellationRequested) return;

                        var state = await Task.Run(() => EvaluateCurrentNetwork());
                        _currentState = state;
                        NetworkSettled?.Invoke(state);
                    }
                    catch (TaskCanceledException)
                    {
                        // Debounce reset - expected
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error("NetworkMonitor", "Evaluation error", ex);
                    }
                }, token);
            }
        }

        public NetworkState EvaluateCurrentNetwork()
        {
            bool isAvailable = NetworkInterface.GetIsNetworkAvailable();
            var (wifiSsid, wifiBssid) = NativeWifiService.GetCurrentConnectionInfo();
            var config = ConfigManager.Instance.Config;

            bool isWireless = !string.IsNullOrWhiteSpace(wifiSsid);
            string activeSsid = wifiSsid ?? string.Empty;

            string adapterName = string.Empty;
            string connectionType = isWireless ? "Wi-Fi" : "Wired Network";
            string? localIp = null;
            string? subnetMask = null;
            string? gatewayIp = null;
            string? gatewayMac = null;
            string? dnsSuffix = null;
            string? dnsServerName = null;
            var detectedNames = new List<string>();

            if (!string.IsNullOrWhiteSpace(activeSsid))
            {
                detectedNames.Add(activeSsid);
            }
            if (!string.IsNullOrWhiteSpace(wifiBssid))
            {
                detectedNames.Add(wifiBssid);
            }

            try
            {
                // 1. Identify primary physical interface
                NetworkInterface? primaryAdapter = FindPrimaryPhysicalAdapter();

                if (primaryAdapter != null)
                {
                    adapterName = primaryAdapter.Name;
                    if (primaryAdapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        connectionType = "Wi-Fi";
                    }
                    else if (primaryAdapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        connectionType = "Ethernet";
                    }
                    else if (primaryAdapter.NetworkInterfaceType == NetworkInterfaceType.Wman ||
                             primaryAdapter.NetworkInterfaceType == NetworkInterfaceType.Wwanpp ||
                             primaryAdapter.NetworkInterfaceType == NetworkInterfaceType.Wwanpp2)
                    {
                        connectionType = "Cellular";
                    }

                    var ipProps = primaryAdapter.GetIPProperties();

                    // DNS suffix
                    if (!string.IsNullOrWhiteSpace(ipProps.DnsSuffix))
                    {
                        dnsSuffix = ipProps.DnsSuffix;
                        if (!detectedNames.Contains(ipProps.DnsSuffix, StringComparer.OrdinalIgnoreCase))
                        {
                            detectedNames.Add(ipProps.DnsSuffix);
                        }
                    }

                    // Local IPv4 and Subnet
                    var unicastIpv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork);
                    if (unicastIpv4 != null)
                    {
                        localIp = unicastIpv4.Address.ToString();
                        subnetMask = unicastIpv4.IPv4Mask?.ToString();
                    }

                    // Default Gateway IPv4 & MAC
                    foreach (var gw in ipProps.GatewayAddresses)
                    {
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !gw.Address.Equals(IPAddress.Any) &&
                            !gw.Address.Equals(IPAddress.None))
                        {
                            gatewayIp = gw.Address.ToString();
                            gatewayMac = ArpService.GetMacAddress(gw.Address);
                            break;
                        }
                    }

                    // DNS Servers (non-blocking)
                    var dnsList = ipProps.DnsAddresses
                        .Where(d => d.AddressFamily == AddressFamily.InterNetwork &&
                                    !d.Equals(IPAddress.Any) &&
                                    !d.Equals(IPAddress.Loopback))
                        .Select(d => d.ToString())
                        .ToList();

                    if (dnsList.Count > 0)
                    {
                        dnsServerName = string.Join(", ", dnsList);
                    }
                }
                else if (!isWireless && !isAvailable)
                {
                    connectionType = "Disconnected";
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NetworkMonitor", "Error querying interfaces", ex);
            }

            // Trust Evaluation
            bool isTrusted = false;
            bool isGatewayMacMismatch = false;
            string? registeredMac = null;

            // Check if any detected name matches trusted networks or stacked names in any trusted gateway
            foreach (var name in detectedNames)
            {
                if (config.TrustedNetworks.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    isTrusted = true;
                    break;
                }

                var matchingGw = config.TrustedGateways.FirstOrDefault(g => g.StackedNames.Contains(name, StringComparer.OrdinalIgnoreCase));
                if (matchingGw != null)
                {
                    if (!string.IsNullOrWhiteSpace(matchingGw.MacAddress) && !string.IsNullOrWhiteSpace(gatewayMac))
                    {
                        if (string.Equals(matchingGw.MacAddress, gatewayMac, StringComparison.OrdinalIgnoreCase))
                        {
                            isTrusted = true;
                            break;
                        }
                        else
                        {
                            isGatewayMacMismatch = true;
                            registeredMac = matchingGw.MacAddress;
                        }
                    }
                    else
                    {
                        isTrusted = true;
                        break;
                    }
                }
            }

            // Check if Gateway IP matches a TrustedGateway
            if (!isTrusted && !string.IsNullOrEmpty(gatewayIp))
            {
                var matchingGw = config.TrustedGateways.FirstOrDefault(g => string.Equals(g.IpAddress, gatewayIp, StringComparison.OrdinalIgnoreCase));
                if (matchingGw != null)
                {
                    if (string.IsNullOrWhiteSpace(matchingGw.MacAddress) || string.IsNullOrWhiteSpace(gatewayMac))
                    {
                        isTrusted = true;
                    }
                    else if (string.Equals(matchingGw.MacAddress, gatewayMac, StringComparison.OrdinalIgnoreCase))
                    {
                        isTrusted = true;
                    }
                    else
                    {
                        isGatewayMacMismatch = true;
                        registeredMac = matchingGw.MacAddress;
                    }
                }
            }

            var result = new NetworkState
            {
                Ssid = activeSsid,
                Bssid = wifiBssid,
                AdapterName = adapterName,
                ConnectionType = connectionType,
                LocalIp = localIp,
                SubnetMask = subnetMask,
                IsConnected = isAvailable && (!string.IsNullOrEmpty(gatewayIp) || isWireless),
                IsWireless = isWireless,
                IsTrusted = isTrusted,
                GatewayIp = gatewayIp,
                GatewayMac = gatewayMac,
                DnsServerName = dnsServerName,
                DnsSuffix = dnsSuffix,
                DetectedNames = detectedNames,
                IsGatewayMacMismatch = isGatewayMacMismatch,
                RegisteredGatewayMac = registeredMac,
                LastCheckedUtc = DateTime.UtcNow
            };

            LoggingService.Instance.Info("NetworkMonitor", 
                $"Network State: {result.ConnectionType}, SSID='{result.Ssid}', Gateway={result.GatewayIp} [{result.GatewayMac}], Trusted={result.IsTrusted}");

            return result;
        }

        private NetworkInterface? FindPrimaryPhysicalAdapter()
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            // Filter out loopback, virtual, and down adapters
            var physicalCandidates = interfaces.Where(ni =>
                ni.OperationalStatus == OperationalStatus.Up &&
                ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                !ni.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) &&
                !ni.Description.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (physicalCandidates.Count == 0)
            {
                return null;
            }

            // Try GetBestInterface to public IP (8.8.8.8 = 0x08080808 in host byte order)
            try
            {
                uint publicDns = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
                if (GetBestInterface(publicDns, out uint bestIfIndex) == 0)
                {
                    var match = physicalCandidates.FirstOrDefault(ni =>
                    {
                        try
                        {
                            return ni.GetIPProperties().GetIPv4Properties()?.Index == bestIfIndex;
                        }
                        catch { return false; }
                    });

                    if (match != null) return match;
                }
            }
            catch { }

            // Fallback: choose physical candidate with an active default IPv4 gateway
            var withGateway = physicalCandidates.FirstOrDefault(ni =>
            {
                try
                {
                    var ipProps = ni.GetIPProperties();
                    return ipProps.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !g.Address.Equals(IPAddress.Any) &&
                        !g.Address.Equals(IPAddress.None));
                }
                catch { return false; }
            });

            return withGateway ?? physicalCandidates.FirstOrDefault();
        }

        /// <summary>
        /// Fast non-blocking check to verify if the physical default gateway is reachable via ARP or ping.
        /// Returns false if physically offline/disconnected.
        /// </summary>
        public async Task<bool> IsLocalGatewayReachableAsync(int timeoutMs = 800)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string? gwIp = CurrentState.GatewayIp;
                    IPAddress? ip = null;
                    if (!string.IsNullOrWhiteSpace(gwIp))
                    {
                        IPAddress.TryParse(gwIp, out ip);
                    }

                    if (ip == null)
                    {
                        var primary = FindPrimaryPhysicalAdapter();
                        var gw = primary?.GetIPProperties().GatewayAddresses
                            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (gw == null) return false;
                        ip = gw.Address;
                    }

                    // 1. ARP probe first (instantaneous, layer 2 local reachability check)
                    string? mac = ArpService.GetMacAddress(ip);
                    if (!string.IsNullOrEmpty(mac))
                    {
                        return true;
                    }

                    // 2. Fast ICMP ping fallback
                    using var ping = new Ping();
                    var reply = ping.Send(ip, timeoutMs);
                    return reply.Status == IPStatus.Success;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
