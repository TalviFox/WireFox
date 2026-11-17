using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WireFox.Services;

namespace WireFox.ViewModels
{
    public class DiagnosticsViewModel : ViewModelBase
    {
        private string _wireguardStatus = "Checking...";
        private string _activeTunnelStatus = "None";
        private string _primaryNetworkStatus = "Checking...";
        private string _gatewayStatus = "Checking...";
        private readonly object _collectionLock = new();
        private int _isUpdatingSummary;
        private int _isRunningScan;

        public ObservableCollection<LogEntry> Logs => LoggingService.Instance.Entries;

        public string WireGuardStatus
        {
            get => _wireguardStatus;
            set => SetProperty(ref _wireguardStatus, value);
        }

        public string ActiveTunnelStatus
        {
            get => _activeTunnelStatus;
            set => SetProperty(ref _activeTunnelStatus, value);
        }

        public string PrimaryNetworkStatus
        {
            get => _primaryNetworkStatus;
            set => SetProperty(ref _primaryNetworkStatus, value);
        }

        public string GatewayStatus
        {
            get => _gatewayStatus;
            set => SetProperty(ref _gatewayStatus, value);
        }

        public bool IsDebugLoggingEnabled
        {
            get => LoggingService.Instance.IsDebugEnabled;
            set
            {
                if (LoggingService.Instance.IsDebugEnabled != value)
                {
                    LoggingService.Instance.IsDebugEnabled = value;
                    ConfigManager.Instance.Config.DebugLogging = value;
                    ConfigManager.Instance.SaveConfig();
                    OnPropertyChanged(nameof(IsDebugLoggingEnabled));
                    LoggingService.Instance.Info("Diagnostics", $"Debug logging {(value ? "enabled" : "disabled")}");
                }
            }
        }

        public ICommand ClearLogsCommand { get; }
        public ICommand CopyLogsCommand { get; }
        public ICommand OpenLogFileCommand { get; }
        public ICommand RunScanCommand { get; }

        public DiagnosticsViewModel()
        {
            BindingOperations.EnableCollectionSynchronization(Logs, _collectionLock);

            ClearLogsCommand = new RelayCommand(() => LoggingService.Instance.Clear());
            CopyLogsCommand = new RelayCommand(CopyLogsToClipboard);
            OpenLogFileCommand = new RelayCommand(OpenLogFile);
            RunScanCommand = new RelayCommand(async () => await RunDiagnosticsScanAsync());

            _ = UpdateSummaryAsync();
            WatchdogService.Instance.TunnelDetailsUpdated += info => { _ = UpdateSummaryAsync(); };
            NetworkMonitorService.Instance.NetworkSettled += state => { _ = UpdateSummaryAsync(); };
        }

        private async Task UpdateSummaryAsync()
        {
            if (Interlocked.CompareExchange(ref _isUpdatingSummary, 1, 0) != 0)
            {
                return;
            }

            try
            {
                var cli = WireGuardCliService.Instance;
                var (isInstalled, active) = await Task.Run(() => (cli.IsInstalled, cli.GetActiveInterfaceNames()));
                var net = NetworkMonitorService.Instance.CurrentState;

                string wgStatus = isInstalled
                    ? "Installed (wg.exe & wireguard.exe)"
                    : "Not Found";
                string activeStatus = active.Count > 0
                    ? string.Join(", ", active)
                    : "None (Inactive)";
                string netStatus = net.IsConnected
                    ? $"{net.ConnectionType} - {(net.IsWireless ? net.Ssid : net.AdapterName)}"
                    : "No Network Connection";
                string gwStatus = !string.IsNullOrEmpty(net.GatewayIp)
                    ? $"{net.GatewayIp} [{net.GatewayMac ?? "No ARP"}]"
                    : "No Gateway Detected";

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        WireGuardStatus = wgStatus;
                        ActiveTunnelStatus = activeStatus;
                        PrimaryNetworkStatus = netStatus;
                        GatewayStatus = gwStatus;
                    });
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("Diagnostics", $"UpdateSummaryAsync error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _isUpdatingSummary, 0);
            }
        }

        private void CopyLogsToClipboard()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var entry in Logs)
                {
                    sb.AppendLine(entry.ToString());
                }
                System.Windows.Clipboard.SetText(sb.ToString());
                LoggingService.Instance.Info("Diagnostics", "Copied logs to clipboard");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Diagnostics", "Failed to copy logs to clipboard", ex);
            }
        }

        private void OpenLogFile()
        {
            try
            {
                string path = LoggingService.Instance.LogFilePath;
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "");
                }
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Diagnostics", "Failed to open log file", ex);
            }
        }

        public async Task RunDiagnosticsScanAsync()
        {
            if (Interlocked.CompareExchange(ref _isRunningScan, 1, 0) != 0)
            {
                return;
            }

            try
            {
                LoggingService.Instance.Info("Diagnostics", "=== STARTING DIAGNOSTIC SYSTEM SCAN ===");

                var cli = WireGuardCliService.Instance;
                LoggingService.Instance.Info("Diagnostics", $"WireGuard Installed: {cli.IsInstalled}");
                LoggingService.Instance.Info("Diagnostics", $"WireGuard Exe: {cli.WireGuardExePath ?? "NOT FOUND"}");
                LoggingService.Instance.Info("Diagnostics", $"WG CLI Exe: {cli.WgExePath ?? "NOT FOUND"}");

                var (discovered, active) = await Task.Run(() => (cli.GetDiscoveredTunnelNames(), cli.GetActiveInterfaceNames()));
                LoggingService.Instance.Info("Diagnostics", $"Discovered Tunnels ({discovered.Count}): {string.Join(", ", discovered)}");
                LoggingService.Instance.Info("Diagnostics", $"Active Interfaces ({active.Count}): {string.Join(", ", active)}");

                var net = NetworkMonitorService.Instance.CurrentState;
                LoggingService.Instance.Info("Diagnostics", 
                    $"Network State: Connected={net.IsConnected}, Type={net.ConnectionType}, Adapter='{net.AdapterName}', LocalIP={net.LocalIp}, Gateway={net.GatewayIp}, GatewayMAC={net.GatewayMac}, BSSID={net.Bssid}");

                LoggingService.Instance.Success("Diagnostics", "=== DIAGNOSTIC SCAN COMPLETE ===");
                await UpdateSummaryAsync();

                if (!cli.IsInstalled)
                {
                    NotificationService.Instance.ShowNotification("Diagnostic Scan Warning", "WireGuard CLI tools not detected on this system.");
                }
                else if (active.Count > 0)
                {
                    string tunnelText = active.Count == 1 ? "1 active tunnel" : $"{active.Count} active tunnels";
                    NotificationService.Instance.ShowNotification("Diagnostic Scan Complete", $"All systems nominal (CLI ready, {tunnelText}).");
                }
                else
                {
                    string profileText = discovered.Count switch
                    {
                        0 => "no tunnel profiles found",
                        1 => "1 tunnel profile found",
                        _ => $"{discovered.Count} tunnel profiles found"
                    };
                    NotificationService.Instance.ShowNotification("Diagnostic Scan Complete", $"Scan complete: WireGuard CLI ready, {profileText}.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Diagnostics", "Diagnostic scan error", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunningScan, 0);
            }
        }
    }
}
