using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WireFox.Services
{
    public class WireGuardTunnelInfo
    {
        public string InterfaceName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsServiceInstalled { get; set; }
        public bool IsServiceRunning { get; set; }
        public string? ConfigPath { get; set; }
        public DateTime? LatestHandshakeUtc { get; set; }
        public string? Endpoint { get; set; }
        public string? AllowedIps { get; set; }
        public long TransferRxBytes { get; set; }
        public long TransferTxBytes { get; set; }

        public string FormattedTransfer
        {
            get
            {
                if (TransferRxBytes == 0 && TransferTxBytes == 0) return "0 B sent, 0 B received";
                return $"{FormatBytes(TransferTxBytes)} sent, {FormatBytes(TransferRxBytes)} received";
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public class WireGuardCliService
    {
        private static readonly Lazy<WireGuardCliService> _instance = new(() => new WireGuardCliService());
        public static WireGuardCliService Instance => _instance.Value;

        private static readonly Regex InterfaceNameRegex = new(@"^[a-zA-Z0-9_=+.-]{1,32}$", RegexOptions.Compiled);
        private static readonly Regex AllowedIpsRegex = new(@"^[0-9a-fA-F:.,/ ]+$", RegexOptions.Compiled);

        public static bool IsValidInterfaceName(string? name) => !string.IsNullOrWhiteSpace(name) && InterfaceNameRegex.IsMatch(name);

        private string? _wireguardExePath;
        private string? _wgExePath;

        public WireGuardCliService()
        {
            DiscoverExecutables();
        }

        public bool IsInstalled => !string.IsNullOrEmpty(_wireguardExePath) || !string.IsNullOrEmpty(_wgExePath);
        public string? WireGuardExePath => _wireguardExePath;
        public string? WgExePath => _wgExePath;

        private void DiscoverExecutables()
        {
            string[] standardDirs = new[]
            {
                @"C:\Program Files\WireGuard",
                @"C:\Program Files (x86)\WireGuard",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard"),
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (var dir in standardDirs)
            {
                if (Directory.Exists(dir))
                {
                    string wgExe = Path.Combine(dir, "wg.exe");
                    string wireguardExe = Path.Combine(dir, "wireguard.exe");

                    if (File.Exists(wgExe) && _wgExePath == null)
                    {
                        _wgExePath = wgExe;
                    }
                    if (File.Exists(wireguardExe) && _wireguardExePath == null)
                    {
                        _wireguardExePath = wireguardExe;
                    }
                }
            }

            if (_wgExePath == null) _wgExePath = FindInPath("wg.exe");
            if (_wireguardExePath == null) _wireguardExePath = FindInPath("wireguard.exe");

            LoggingService.Instance.Info("WireGuardCli", 
                $"Executables Discovered: wireguard.exe='{_wireguardExePath}', wg.exe='{_wgExePath}'");
        }

        private string? FindInPath(string filename)
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (var path in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string fullPath = Path.Combine(path.Trim(), filename);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
            return null;
        }

        public string GetServiceName(string interfaceName) => $"WireGuardTunnel${interfaceName}";

        /// <summary>
        /// Queries wg.exe for actively running interfaces in the kernel.
        /// </summary>
        public List<string> GetActiveInterfaceNames()
        {
            var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(_wgExePath))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _wgExePath,
                        Arguments = "show interfaces",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(3000);

                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            var names = output.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var n in names)
                            {
                                active.Add(n.Trim());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("WireGuardCli", "GetActiveInterfaceNames failed", ex);
                }
            }

            // Also check network adapters for WireGuard Tunnel adapters that are UP
            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase) ||
                         ni.Description.Contains("Wintun", StringComparison.OrdinalIgnoreCase)))
                    {
                        active.Add(ni.Name);
                    }
                }
            }
            catch { }

            return active.ToList();
        }

        /// <summary>
        /// Returns all available tunnels from running interfaces, installed services, and detected .conf files.
        /// </summary>
        public List<string> GetDiscoveredTunnelNames()
        {
            var tunnels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Active interfaces
            foreach (var act in GetActiveInterfaceNames())
            {
                tunnels.Add(act);
            }

            // 2. Running or installed tunnel services
            try
            {
                var services = ServiceController.GetServices();
                foreach (var s in services)
                {
                    if (s.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase))
                    {
                        tunnels.Add(s.ServiceName.Substring("WireGuardTunnel$".Length));
                    }
                }
            }
            catch { }

            // 3. Scan common folders for .conf files (Downloads, Desktop, Documents, App Directory)
            var candidateFolders = new List<string>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            foreach (var folder in candidateFolders)
            {
                try
                {
                    if (Directory.Exists(folder))
                    {
                        foreach (var file in Directory.GetFiles(folder, "*.conf", SearchOption.TopDirectoryOnly))
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            if (IsValidInterfaceName(name))
                            {
                                // Sanity check: Ensure candidate file contains valid [Interface] header
                                try
                                {
                                    var firstLines = File.ReadLines(file).Take(15);
                                    if (firstLines.Any(l => l.Trim().StartsWith("[Interface]", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        tunnels.Add(name);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }

            // 4. Check DPAPI configs if accessible
            try
            {
                string configDir = @"C:\Program Files\WireGuard\Data\Configurations";
                if (Directory.Exists(configDir))
                {
                    foreach (var file in Directory.GetFiles(configDir, "*.conf.dpapi"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
                        {
                            name = Path.GetFileNameWithoutExtension(name);
                        }
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            tunnels.Add(name);
                        }
                    }
                }
            }
            catch { }

            LoggingService.Instance.Debug("WireGuardCli", $"Discovered tunnels: {string.Join(", ", tunnels)}");
            return tunnels.ToList();
        }

        public string? ResolveConfigPath(string interfaceName, string? suggestedPath = null)
        {
            if (!string.IsNullOrEmpty(suggestedPath) && File.Exists(suggestedPath))
            {
                return suggestedPath;
            }

            var candidatePaths = new List<string>
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{interfaceName}.conf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", $"{interfaceName}.conf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{interfaceName}.conf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"{interfaceName}.conf"),
                $@"C:\Program Files\WireGuard\Data\Configurations\{interfaceName}.conf.dpapi"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    if (File.Exists(path)) return path;
                }
                catch { }
            }

            // Search Downloads for case-insensitive match or contains
            try
            {
                string dlFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(dlFolder))
                {
                    var match = Directory.GetFiles(dlFolder, "*.conf")
                        .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            catch { }

            return null;
        }

        public bool IsTunnelServiceRunning(string interfaceName)
        {
            if (string.IsNullOrWhiteSpace(interfaceName)) return false;

            // Check active kernel interface first
            var activeInterfaces = GetActiveInterfaceNames();
            if (activeInterfaces.Contains(interfaceName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            // Check Windows service
            try
            {
                string serviceName = GetServiceName(interfaceName);
                var services = ServiceController.GetServices();
                var sc = services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
                
                if (sc != null)
                {
                    return sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("WireGuardCli", $"IsTunnelServiceRunning check error: {ex.Message}");
            }
            return false;
        }

        public async Task<bool> StartTunnelAsync(string interfaceName, string? configPath = null)
        {
            if (!IsValidInterfaceName(interfaceName) || !IsInstalled)
            {
                LoggingService.Instance.Warning("WireGuardCli", $"Cannot start tunnel: Interface='{interfaceName}' is invalid or WireGuard is not installed.");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    string serviceName = GetServiceName(interfaceName);
                    LoggingService.Instance.Info("WireGuardCli", $"Attempting to start tunnel '{interfaceName}' (Service: {serviceName})...");
                    
                    // 1. Check if service already exists
                    try
                    {
                        var services = ServiceController.GetServices();
                        var existingService = services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

                        if (existingService != null)
                        {
                            if (existingService.Status == ServiceControllerStatus.Running)
                            {
                                LoggingService.Instance.Info("WireGuardCli", $"Tunnel service '{serviceName}' is already running.");
                                return true;
                            }
                            
                            if (existingService.Status == ServiceControllerStatus.Stopped)
                            {
                                LoggingService.Instance.Info("WireGuardCli", $"Starting existing service '{serviceName}'...");
                                existingService.Start();
                                existingService.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                                LoggingService.Instance.Success("WireGuardCli", $"Tunnel service '{serviceName}' started successfully.");
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warning("WireGuardCli", $"Existing service start attempt failed: {ex.Message}");
                    }

                    // 2. Service does not exist yet; find configuration file to install it
                    if (!string.IsNullOrEmpty(_wireguardExePath))
                    {
                        string? targetConfig = ResolveConfigPath(interfaceName, configPath);

                        if (string.IsNullOrEmpty(targetConfig) || !File.Exists(targetConfig))
                        {
                            LoggingService.Instance.Error("WireGuardCli", 
                                $"No configuration (.conf) file found for '{interfaceName}'. Please select a valid config in Settings.");
                            return false;
                        }

                        LoggingService.Instance.Info("WireGuardCli", 
                            $"Installing tunnel service using '{targetConfig}'...");

                        var psi = new ProcessStartInfo
                        {
                            FileName = _wireguardExePath,
                            Arguments = $"/installtunnelservice \"{targetConfig}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);

                        bool running = IsTunnelServiceRunning(interfaceName);
                        if (running)
                        {
                            LoggingService.Instance.Success("WireGuardCli", $"Tunnel '{interfaceName}' installed and running.");
                        }
                        else
                        {
                            LoggingService.Instance.Warning("WireGuardCli", $"Install completed but service is not running for '{interfaceName}'.");
                        }
                        return running;
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("WireGuardCli", $"StartTunnel failed for '{interfaceName}'", ex);
                }
                return false;
            });
        }

        public async Task<bool> StopTunnelAsync(string interfaceName)
        {
            if (!IsValidInterfaceName(interfaceName) || !IsInstalled) return false;

            return await Task.Run(() =>
            {
                try
                {
                    string serviceName = GetServiceName(interfaceName);
                    LoggingService.Instance.Info("WireGuardCli", $"Attempting to stop tunnel '{interfaceName}' (Service: {serviceName})...");
                    
                    ServiceController? existingService = null;
                    try
                    {
                        var services = ServiceController.GetServices();
                        existingService = services.FirstOrDefault(s => string.Equals(s.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Debug("WireGuardCli", $"Service lookup exception: {ex.Message}");
                    }

                    // CRITICAL FIX: If service does NOT exist, do NOT call /uninstalltunnelservice!
                    // Calling /uninstalltunnelservice on a nonexistent service causes wireguard.exe to pop up
                    // the error dialog: "The specified service does not exist as an installed service."
                    if (existingService == null)
                    {
                        if (!IsTunnelServiceRunning(interfaceName))
                        {
                            LoggingService.Instance.Info("WireGuardCli", 
                                $"Service '{serviceName}' does not exist as an installed Windows service. Nothing to stop/uninstall.");
                            return true;
                        }
                        else
                        {
                            LoggingService.Instance.Info("WireGuardCli", 
                                $"Service '{serviceName}' does not exist, but tunnel interface is active. Proceeding to cleanup.");
                        }
                    }
                    else
                    {
                        if (existingService.Status == ServiceControllerStatus.Stopped)
                        {
                            LoggingService.Instance.Info("WireGuardCli", $"Service '{serviceName}' is already stopped.");
                        }
                        else if (existingService.CanStop)
                        {
                            LoggingService.Instance.Info("WireGuardCli", $"Stopping service '{serviceName}'...");
                            existingService.Stop();
                            existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(5));
                            LoggingService.Instance.Success("WireGuardCli", $"Service '{serviceName}' stopped.");
                        }
                    }

                    // If still running or if we wish to clean up the service
                    if (!string.IsNullOrEmpty(_wireguardExePath) && IsTunnelServiceRunning(interfaceName))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = _wireguardExePath,
                            Arguments = $"/uninstalltunnelservice \"{interfaceName}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        proc?.WaitForExit(5000);
                    }

                    bool stopped = !IsTunnelServiceRunning(interfaceName);
                    LoggingService.Instance.Info("WireGuardCli", $"Tunnel '{interfaceName}' stop result: Stopped={stopped}");
                    return stopped;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("WireGuardCli", $"StopTunnel failed for '{interfaceName}'", ex);
                }
                return false;
            });
        }

        public async Task<WireGuardTunnelInfo> GetTunnelDetailsAsync(string interfaceName)
        {
            var info = new WireGuardTunnelInfo
            {
                InterfaceName = interfaceName,
                IsActive = IsTunnelServiceRunning(interfaceName)
            };

            if (string.IsNullOrEmpty(_wgExePath) || !IsValidInterfaceName(interfaceName) || !info.IsActive)
            {
                return info;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _wgExePath,
                        Arguments = $"show \"{interfaceName}\" dump",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return info;

                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        // Line 0 is Interface dump: private-key public-key listen-port fwmark
                        // Line 1+ is Peer dump: public-key preshared-key endpoint allowed-ips latest-handshake rx-bytes tx-bytes keepalive
                        foreach (var line in lines.Skip(1))
                        {
                            var parts = line.Split('\t');
                            if (parts.Length >= 7)
                            {
                                info.Endpoint = parts[2];
                                info.AllowedIps = parts[3];

                                if (long.TryParse(parts[4], out long epoch) && epoch > 0)
                                {
                                    info.LatestHandshakeUtc = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
                                }

                                if (long.TryParse(parts[5], out long rx)) info.TransferRxBytes += rx;
                                if (long.TryParse(parts[6], out long tx)) info.TransferTxBytes += tx;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Debug("WireGuardCli", $"GetTunnelDetails failed for '{interfaceName}': {ex.Message}");
                }
                return info;
            });
        }

        public async Task<DateTime?> GetLatestHandshakeUtcAsync(string interfaceName)
        {
            var details = await GetTunnelDetailsAsync(interfaceName);
            return details.LatestHandshakeUtc;
        }

        public async Task<bool> SetSplitTunnelAllowedIpsAsync(string interfaceName, string allowedIps)
        {
            if (string.IsNullOrEmpty(_wgExePath) || !IsValidInterfaceName(interfaceName) || 
                string.IsNullOrWhiteSpace(allowedIps) || !AllowedIpsRegex.IsMatch(allowedIps))
            {
                LoggingService.Instance.Warning("WireGuardCli", $"SetSplitTunnelAllowedIps invalid arguments: Interface='{interfaceName}', AllowedIPs='{allowedIps}'");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = _wgExePath,
                        Arguments = $"show \"{interfaceName}\" peers",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return false;

                    string peerKey = proc.StandardOutput.ReadLine()?.Trim() ?? string.Empty;
                    proc.WaitForExit(3000);

                    if (string.IsNullOrEmpty(peerKey)) return false;

                    var setPsi = new ProcessStartInfo
                    {
                        FileName = _wgExePath,
                        Arguments = $"set \"{interfaceName}\" peer \"{peerKey}\" allowed-ips {allowedIps}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var setProc = Process.Start(setPsi);
                    setProc?.WaitForExit(3000);
                    bool ok = setProc?.ExitCode == 0;
                    LoggingService.Instance.Info("WireGuardCli", $"SetSplitTunnelAllowedIps for '{interfaceName}' returned: {ok}");
                    return ok;
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error("WireGuardCli", "SetSplitTunnelAllowedIps failed", ex);
                    return false;
                }
            });
        }

        public string GetDefaultAllowedIps(string interfaceName, string? configPath = null)
        {
            try
            {
                // 1. Try reading from active interface if running
                if (IsTunnelServiceRunning(interfaceName))
                {
                    var details = GetTunnelDetailsAsync(interfaceName).GetAwaiter().GetResult();
                    if (!string.IsNullOrWhiteSpace(details.AllowedIps))
                    {
                        return details.AllowedIps;
                    }
                }

                // 2. Try parsing from .conf file
                string? resolvedPath = ResolveConfigPath(interfaceName, configPath);
                if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
                {
                    var lines = File.ReadAllLines(resolvedPath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("AllowedIPs", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = trimmed.Split('=', 2);
                            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                            {
                                return parts[1].Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("WireGuardCli", $"GetDefaultAllowedIps check exception: {ex.Message}");
            }

            return "0.0.0.0/0, ::/0";
        }
    }
}
