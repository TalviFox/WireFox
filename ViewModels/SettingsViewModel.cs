using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using WireFox.Models;
using WireFox.Services;
using Wpf.Ui.Appearance;

namespace WireFox.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private string _interfaceName = "wg0";
        private string? _currentLoadedInterface;
        private string? _wireGuardConfigPath;
        private int _handshakeTimeoutSeconds = 180;
        private string _splitTunnelAllowedIPs = "";
        private string _fullTunnelAllowedIPs = "0.0.0.0/0, ::/0";
        private string _selectedTheme = "system";
        private bool _startMinimized;
        private bool _startWithWindows;
        private bool _isRefreshingTunnels;
        private bool _isInitialSetup;
        private bool _checkForUpdates = true;
        private bool _isCheckingForUpdates;
        private bool _isUpdateAvailable;
        private bool _isUpdating;
        private string _updateStatusText = "Up to date";
        private string _updateProgressText = "";
        private string _newVersionTag = "";

        public event Action? InitialSetupFinished;

        public bool IsInitialSetup
        {
            get => _isInitialSetup;
            set => SetProperty(ref _isInitialSetup, value);
        }

        public string InitialSetupPromptText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(InterfaceName))
                    return "No tunnel selected yet. Choose a discovered tunnel or browse for a .conf file below:";
                return $"Default tunnel will be set to: {InterfaceName}";
            }
        }

        public string ConfiguredTunnelHeader => string.IsNullOrWhiteSpace(InterfaceName)
            ? "Tunnel Configuration"
            : $"Tunnel Configuration ({InterfaceName})";

        public List<string> Themes { get; } = new() { "System", "Light", "Dark" };
        public ObservableCollection<string> AvailableTunnels { get; } = new();

        public string InterfaceName
        {
            get => _interfaceName;
            set
            {
                if (string.IsNullOrWhiteSpace(value) && _isRefreshingTunnels) return;
                
                string newName = value?.Trim() ?? "";
                if (_interfaceName != newName)
                {
                    // Save previous tunnel profile before switching
                    if (!_isRefreshingTunnels && !string.IsNullOrWhiteSpace(_currentLoadedInterface))
                    {
                        SaveCurrentTunnelProfile();
                    }

                    _interfaceName = newName;
                    OnPropertyChanged(nameof(InterfaceName));
                    OnPropertyChanged(nameof(InitialSetupPromptText));
                    OnPropertyChanged(nameof(ConfiguredTunnelHeader));

                    if (!_isRefreshingTunnels && !string.IsNullOrWhiteSpace(newName))
                    {
                        LoadTunnelProfile(newName);
                        AutoSave();
                    }
                }
            }
        }

        public string? WireGuardConfigPath
        {
            get => _wireGuardConfigPath;
            set
            {
                if (SetProperty(ref _wireGuardConfigPath, value))
                {
                    AutoSave();
                }
            }
        }

        public int HandshakeTimeoutSeconds
        {
            get => _handshakeTimeoutSeconds;
            set
            {
                if (SetProperty(ref _handshakeTimeoutSeconds, value))
                {
                    AutoSave();
                }
            }
        }

        public string FullTunnelAllowedIPs
        {
            get => _fullTunnelAllowedIPs;
            set
            {
                if (SetProperty(ref _fullTunnelAllowedIPs, value))
                {
                    AutoSave();
                }
            }
        }

        public string SplitTunnelAllowedIPs
        {
            get => _splitTunnelAllowedIPs;
            set
            {
                if (SetProperty(ref _splitTunnelAllowedIPs, value))
                {
                    AutoSave();
                }
            }
        }

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (SetProperty(ref _selectedTheme, value))
                {
                    ApplyTheme(value);
                    AutoSave();
                }
            }
        }

        public bool StartMinimized
        {
            get => _startMinimized;
            set
            {
                if (SetProperty(ref _startMinimized, value))
                {
                    AutoSave();
                }
            }
        }

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (value && !StartupService.IsRunningFromProgramFiles)
                {
                    System.Windows.MessageBox.Show(
                        "Start with Windows is disabled in portable mode to prevent privilege escalation.\n\nPlease click 'Install to Program Files' first.",
                        "Portable Mode Notice",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                    OnPropertyChanged(nameof(StartWithWindows));
                    return;
                }

                if (SetProperty(ref _startWithWindows, value))
                {
                    StartupService.SetStartup(value);
                    AutoSave();
                }
            }
        }

        public bool CheckForUpdates
        {
            get => _checkForUpdates;
            set
            {
                if (SetProperty(ref _checkForUpdates, value))
                {
                    ConfigManager.Instance.Config.CheckForUpdates = value;
                    ConfigManager.Instance.SaveConfig();
                }
            }
        }

        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set => SetProperty(ref _isCheckingForUpdates, value);
        }

        public bool IsUpdateAvailable
        {
            get => _isUpdateAvailable;
            set => SetProperty(ref _isUpdateAvailable, value);
        }

        public bool IsUpdating
        {
            get => _isUpdating;
            set => SetProperty(ref _isUpdating, value);
        }

        public string UpdateStatusText
        {
            get => _updateStatusText;
            set => SetProperty(ref _updateStatusText, value);
        }

        public string UpdateProgressText
        {
            get => _updateProgressText;
            set => SetProperty(ref _updateProgressText, value);
        }

        public string NewVersionTag
        {
            get => _newVersionTag;
            set => SetProperty(ref _newVersionTag, value);
        }

        public string CurrentVersion => "v" + UpdateService.Instance.GetCurrentVersionString();
        public string LocalHash => UpdateService.Instance.GetLocalExecutableHash();
        public string ShortHash => LocalHash.Length > 16 ? $"{LocalHash[..8]}...{LocalHash[^8..]}" : LocalHash;

        public bool IsRunningFromProgramFiles => StartupService.IsRunningFromProgramFiles;
        public bool IsPortableMode => !StartupService.IsRunningFromProgramFiles;
        public bool CanEnableStartup => StartupService.IsRunningFromProgramFiles;

        public ICommand SaveSettingsCommand { get; }
        public ICommand RefreshTunnelsCommand { get; }
        public ICommand BrowseConfigFileCommand { get; }
        public ICommand CompleteInitialSetupCommand { get; }
        public ICommand DetectAllowedIpsCommand { get; }
        public ICommand ResetFullTunnelIpsCommand { get; }
        public ICommand ClearSplitTunnelIpsCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand ApplyUpdateCommand { get; }
        public ICommand RunAuditCommand { get; }
        public ICommand CopyHashCommand { get; }
        public ICommand UninstallCommand { get; }
        public ICommand InstallToProgramFilesCommand { get; }

        public SettingsViewModel()
        {
            SaveSettingsCommand = new RelayCommand(AutoSave);
            RefreshTunnelsCommand = new RelayCommand(RefreshAvailableTunnels);
            BrowseConfigFileCommand = new RelayCommand(BrowseConfigFile);
            CompleteInitialSetupCommand = new RelayCommand(CompleteInitialSetup);
            DetectAllowedIpsCommand = new RelayCommand(DetectDefaultAllowedIps);
            ResetFullTunnelIpsCommand = new RelayCommand(ResetFullTunnelAllowedIps);
            ClearSplitTunnelIpsCommand = new RelayCommand(ClearSplitTunnelAllowedIps);
            CheckForUpdatesCommand = new RelayCommand(async () => await ExecuteCheckForUpdatesAsync());
            ApplyUpdateCommand = new RelayCommand(async () => await ExecuteApplyUpdateAsync());
            RunAuditCommand = new RelayCommand(() => UpdateService.Instance.LaunchExternalAuditConsole());
            CopyHashCommand = new RelayCommand(CopyHashToClipboard);
            UninstallCommand = new RelayCommand(ExecuteUninstall);
            InstallToProgramFilesCommand = new RelayCommand(ExecuteInstallToProgramFiles);

            LoadSettings();
            RefreshAvailableTunnels();
        }

        private void CompleteInitialSetup()
        {
            if (string.IsNullOrWhiteSpace(InterfaceName))
            {
                LoggingService.Instance.Warning("Setup", "Please select or type a WireGuard tunnel interface name.");
                return;
            }

            var config = ConfigManager.Instance.Config;
            config.WireGuardInterface = InterfaceName.Trim();
            
            var profile = config.GetProfile(config.WireGuardInterface);
            profile.WireGuardConfigPath = WireGuardConfigPath;
            profile.HandshakeTimeoutSeconds = HandshakeTimeoutSeconds;
            profile.FullTunnelAllowedIPs = FullTunnelAllowedIPs;
            profile.SplitTunnelAllowedIPs = SplitTunnelAllowedIPs;
            config.SetProfile(config.WireGuardInterface, profile);

            config.InitialSetupCompleted = true;
            ConfigManager.Instance.Save(config);

            IsInitialSetup = false;
            OnPropertyChanged(nameof(IsInitialSetup));
            LoggingService.Instance.Success("Setup", $"Default WireGuard tunnel configured as '{config.WireGuardInterface}'");

            InitialSetupFinished?.Invoke();
        }

        private void BrowseConfigFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select WireGuard Configuration",
                Filter = "WireGuard Configuration (*.conf)|*.conf|All Files (*.*)|*.*",
                InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FileName;
                string tunnelName = Path.GetFileNameWithoutExtension(selectedPath);

                if (!AvailableTunnels.Contains(tunnelName))
                {
                    AvailableTunnels.Add(tunnelName);
                }

                _interfaceName = tunnelName;
                _wireGuardConfigPath = selectedPath;
                _currentLoadedInterface = tunnelName;

                // Auto-detect Allowed IPs from .conf
                string detected = WireGuardCliService.Instance.GetDefaultAllowedIps(tunnelName, selectedPath);
                if (!string.IsNullOrWhiteSpace(detected))
                {
                    _fullTunnelAllowedIPs = detected;
                }

                OnPropertyChanged(nameof(InterfaceName));
                OnPropertyChanged(nameof(WireGuardConfigPath));
                OnPropertyChanged(nameof(FullTunnelAllowedIPs));
                OnPropertyChanged(nameof(InitialSetupPromptText));
                OnPropertyChanged(nameof(ConfiguredTunnelHeader));

                AutoSave();
                LoggingService.Instance.Success("Settings", $"Selected configuration file: {selectedPath} for tunnel '{tunnelName}'");
            }
        }

        private void DetectDefaultAllowedIps()
        {
            if (string.IsNullOrWhiteSpace(InterfaceName)) return;

            string detected = WireGuardCliService.Instance.GetDefaultAllowedIps(InterfaceName, WireGuardConfigPath);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                FullTunnelAllowedIPs = detected;
                LoggingService.Instance.Info("Settings", $"Detected AllowedIPs for '{InterfaceName}': {detected}");
            }
            else
            {
                FullTunnelAllowedIPs = "0.0.0.0/0, ::/0";
                LoggingService.Instance.Info("Settings", $"Defaulted Full-Tunnel AllowedIPs for '{InterfaceName}' to 0.0.0.0/0, ::/0");
            }
        }

        private void ResetFullTunnelAllowedIps()
        {
            FullTunnelAllowedIPs = "0.0.0.0/0, ::/0";
            LoggingService.Instance.Info("Settings", $"Reset Full-Tunnel Allowed IPs for '{InterfaceName}' to default (0.0.0.0/0, ::/0)");
        }

        private void ClearSplitTunnelAllowedIps()
        {
            SplitTunnelAllowedIPs = "";
            LoggingService.Instance.Info("Settings", $"Cleared Split-Tunnel Allowed IPs for '{InterfaceName}'");
        }

        private void RefreshAvailableTunnels()
        {
            _isRefreshingTunnels = true;
            try
            {
                string current = InterfaceName;
                AvailableTunnels.Clear();
                var discovered = WireGuardCliService.Instance.GetDiscoveredTunnelNames();
                foreach (var t in discovered)
                {
                    if (!AvailableTunnels.Contains(t))
                    {
                        AvailableTunnels.Add(t);
                    }
                }
                if (!string.IsNullOrWhiteSpace(current) && !AvailableTunnels.Contains(current))
                {
                    AvailableTunnels.Add(current);
                }
                if (AvailableTunnels.Count == 0)
                {
                    AvailableTunnels.Add("wg0");
                }

                // If on initial setup, auto-select first discovered tunnel
                if (_isInitialSetup && discovered.Count > 0)
                {
                    if (string.IsNullOrWhiteSpace(current) || (current == "wg0" && !discovered.Contains("wg0")))
                    {
                        current = discovered[0];
                    }
                }

                _interfaceName = current;
                _currentLoadedInterface = current;
                LoadTunnelProfile(current);

                OnPropertyChanged(nameof(InterfaceName));
                OnPropertyChanged(nameof(InitialSetupPromptText));
                OnPropertyChanged(nameof(ConfiguredTunnelHeader));
            }
            finally
            {
                _isRefreshingTunnels = false;
            }
        }

        private void LoadSettings()
        {
            var config = ConfigManager.Instance.Config;
            _interfaceName = config.WireGuardInterface;
            _currentLoadedInterface = _interfaceName;

            LoadTunnelProfile(_interfaceName);

            string themeLower = config.Theme?.ToLowerInvariant() ?? "system";
            if (themeLower == "light") _selectedTheme = "Light";
            else if (themeLower == "dark") _selectedTheme = "Dark";
            else _selectedTheme = "System";
            _startMinimized = config.StartMinimized;
            _startWithWindows = StartupService.IsStartupEnabled() || config.StartWithWindows;
            _checkForUpdates = config.CheckForUpdates;
            OnPropertyChanged(nameof(CheckForUpdates));

            _isInitialSetup = ConfigManager.Instance.IsFirstRun || !config.InitialSetupCompleted;
            OnPropertyChanged(nameof(IsInitialSetup));
            OnPropertyChanged(nameof(InitialSetupPromptText));
            OnPropertyChanged(nameof(ConfiguredTunnelHeader));

            ApplyTheme(_selectedTheme);
        }

        private async Task ExecuteCheckForUpdatesAsync()
        {
            if (IsCheckingForUpdates || IsUpdating) return;
            IsCheckingForUpdates = true;
            UpdateStatusText = "Checking GitHub Releases...";

            try
            {
                var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
                if (release != null)
                {
                    IsUpdateAvailable = true;
                    NewVersionTag = release.TagName;
                    UpdateStatusText = $"New release available: {release.TagName}";
                }
                else
                {
                    IsUpdateAvailable = false;
                    UpdateStatusText = "WireFox is up to date.";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText = $"Check failed: {ex.Message}";
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        private async Task ExecuteApplyUpdateAsync()
        {
            if (IsUpdating) return;
            var release = UpdateService.Instance.LatestRelease;
            if (release == null) return;

            IsUpdating = true;
            UpdateProgressText = "Preparing update...";

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    UpdateProgressText = msg;
                });

                await UpdateService.Instance.ExecuteUpdateAsync(release, progress);
            }
            catch (Exception ex)
            {
                IsUpdating = false;
                UpdateProgressText = $"Update failed: {ex.Message}";
                System.Windows.MessageBox.Show($"Update failed:\n{ex.Message}", "WireFox Update", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CopyHashToClipboard()
        {
            try
            {
                System.Windows.Clipboard.SetText(LocalHash);
                UpdateStatusText = "SHA-256 hash copied to clipboard!";
            }
            catch { }
        }

        private void ExecuteInstallToProgramFiles()
        {
            var result = System.Windows.MessageBox.Show(
                "Install WireFox to Program Files?\n\nThis enables secure automatic startup with Windows, seamless in-place updates, and registers WireFox in Windows Installed Apps.",
                "Install WireFox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                if (!StartupService.InstallToProgramFiles())
                {
                    System.Windows.MessageBox.Show("Installation failed. Please verify Administrator privileges.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteUninstall()
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to completely uninstall WireFox?\n\nThis will stop the background daemon, remove the scheduled task, and remove the installation.",
                "Uninstall WireFox",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes) return;

            try
            {
                string programFilesUninstall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireFox", "uninstall.ps1");
                string script = File.Exists(programFilesUninstall)
                    ? programFilesUninstall
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uninstall.ps1");

                ProcessStartInfo psi;
                if (File.Exists(script))
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                }
                else
                {
                    // Safe download to file instead of piping to iex
                    string tempScript = Path.Combine(Path.GetTempPath(), "WireFox_uninstall.ps1");
                    string downloadCmd = $"Write-Host 'Fetching official WireFox uninstaller from GitHub...' -ForegroundColor Cyan; " +
                                         $"Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/TalviFox/WireFox/main/uninstall.ps1' -OutFile '{tempScript}' -UseBasicParsing; " +
                                         $"& '{tempScript}'";

                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{downloadCmd}\"",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                }

                Process.Start(psi);
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Settings", "Failed to launch uninstaller", ex);
                System.Windows.MessageBox.Show($"Could not launch uninstaller:\n{ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadTunnelProfile(string tunnelName)
        {
            var config = ConfigManager.Instance.Config;
            var profile = config.GetProfile(tunnelName);

            _wireGuardConfigPath = profile.WireGuardConfigPath;
            _handshakeTimeoutSeconds = profile.HandshakeTimeoutSeconds;
            _splitTunnelAllowedIPs = profile.SplitTunnelAllowedIPs ?? "";
            _fullTunnelAllowedIPs = string.IsNullOrWhiteSpace(profile.FullTunnelAllowedIPs) ? "0.0.0.0/0, ::/0" : profile.FullTunnelAllowedIPs;
            _currentLoadedInterface = tunnelName;

            OnPropertyChanged(nameof(WireGuardConfigPath));
            OnPropertyChanged(nameof(HandshakeTimeoutSeconds));
            OnPropertyChanged(nameof(SplitTunnelAllowedIPs));
            OnPropertyChanged(nameof(FullTunnelAllowedIPs));
        }

        private void SaveCurrentTunnelProfile()
        {
            if (string.IsNullOrWhiteSpace(_currentLoadedInterface)) return;

            var config = ConfigManager.Instance.Config;
            var profile = config.GetProfile(_currentLoadedInterface);

            profile.WireGuardConfigPath = WireGuardConfigPath;
            profile.HandshakeTimeoutSeconds = HandshakeTimeoutSeconds > 0 ? HandshakeTimeoutSeconds : 180;
            profile.SplitTunnelAllowedIPs = SplitTunnelAllowedIPs?.Trim() ?? "";
            profile.FullTunnelAllowedIPs = string.IsNullOrWhiteSpace(FullTunnelAllowedIPs) ? "0.0.0.0/0, ::/0" : FullTunnelAllowedIPs.Trim();

            config.SetProfile(_currentLoadedInterface, profile);
        }

        private void AutoSave()
        {
            if (_isRefreshingTunnels) return;

            var config = ConfigManager.Instance.Config;
            string effectiveTunnel = string.IsNullOrWhiteSpace(InterfaceName) ? "wg0" : InterfaceName.Trim();
            config.WireGuardInterface = effectiveTunnel;

            var profile = config.GetProfile(effectiveTunnel);
            profile.WireGuardConfigPath = WireGuardConfigPath;
            profile.HandshakeTimeoutSeconds = HandshakeTimeoutSeconds > 0 ? HandshakeTimeoutSeconds : 180;
            profile.SplitTunnelAllowedIPs = SplitTunnelAllowedIPs?.Trim() ?? "";
            profile.FullTunnelAllowedIPs = string.IsNullOrWhiteSpace(FullTunnelAllowedIPs) ? "0.0.0.0/0, ::/0" : FullTunnelAllowedIPs.Trim();
            config.SetProfile(effectiveTunnel, profile);

            config.Theme = SelectedTheme;
            config.StartMinimized = StartMinimized;
            config.StartWithWindows = StartWithWindows;

            ConfigManager.Instance.Save(config);
        }

        private void ApplyTheme(string theme)
        {
            try
            {
                switch (theme.ToLowerInvariant())
                {
                    case "light":
                        ApplicationThemeManager.Apply(ApplicationTheme.Light);
                        break;
                    case "dark":
                        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                        break;
                    case "system":
                    default:
                        ApplicationThemeManager.ApplySystemTheme();
                        break;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Settings", "ApplyTheme error", ex);
            }
        }
    }
}
