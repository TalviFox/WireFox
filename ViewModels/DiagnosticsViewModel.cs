using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
            RunScanCommand = new RelayCommand(RunDiagnosticsScan);

            UpdateSummary();
            WatchdogService.Instance.TunnelDetailsUpdated += _ => UpdateSummary();
            NetworkMonitorService.Instance.NetworkSettled += _ => UpdateSummary();
        }

        private void UpdateSummary()
        {
            var cli = WireGuardCliService.Instance;
            WireGuardStatus = cli.IsInstalled 
                ? "Installed (wg.exe & wireguard.exe)" 
                : "Not Found";

            var active = cli.GetActiveInterfaceNames();
            ActiveTunnelStatus = active.Count > 0 
                ? string.Join(", ", active) 
                : "None (Inactive)";

            var net = NetworkMonitorService.Instance.CurrentState;
            PrimaryNetworkStatus = net.IsConnected
                ? $"{net.ConnectionType} - {(net.IsWireless ? net.Ssid : net.AdapterName)}"
                : "No Network Connection";

            GatewayStatus = !string.IsNullOrEmpty(net.GatewayIp)
                ? $"{net.GatewayIp} [{net.GatewayMac ?? "No ARP"}]"
                : "No Gateway Detected";
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

        public void RunDiagnosticsScan()
        {
            LoggingService.Instance.Info("Diagnostics", "=== STARTING DIAGNOSTIC SYSTEM SCAN ===");

            var cli = WireGuardCliService.Instance;
            LoggingService.Instance.Info("Diagnostics", $"WireGuard Installed: {cli.IsInstalled}");
            LoggingService.Instance.Info("Diagnostics", $"WireGuard Exe: {cli.WireGuardExePath ?? "NOT FOUND"}");
            LoggingService.Instance.Info("Diagnostics", $"WG CLI Exe: {cli.WgExePath ?? "NOT FOUND"}");

            var discovered = cli.GetDiscoveredTunnelNames();
            LoggingService.Instance.Info("Diagnostics", $"Discovered Tunnels ({discovered.Count}): {string.Join(", ", discovered)}");

            var active = cli.GetActiveInterfaceNames();
            LoggingService.Instance.Info("Diagnostics", $"Active Interfaces ({active.Count}): {string.Join(", ", active)}");

            var net = NetworkMonitorService.Instance.CurrentState;
            LoggingService.Instance.Info("Diagnostics", 
                $"Network State: Connected={net.IsConnected}, Type={net.ConnectionType}, Adapter='{net.AdapterName}', LocalIP={net.LocalIp}, Gateway={net.GatewayIp}, GatewayMAC={net.GatewayMac}, BSSID={net.Bssid}");

            LoggingService.Instance.Success("Diagnostics", "=== DIAGNOSTIC SCAN COMPLETE ===");
            UpdateSummary();
        }
    }
}
