using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using WireFox.Models;
using WireFox.Services;
using WireFox.ViewModels;
using Wpf.Ui.Controls;

namespace WireFox.Views
{
    public partial class MainWindow : FluentWindow
    {
        private readonly MainViewModel _viewModel;
        private readonly NotifyIcon _trayIcon;
        private readonly System.Drawing.Icon _defaultIcon;
        private readonly System.Drawing.Icon? _greenIcon;
        private readonly System.Drawing.Icon? _orangeIcon;
        private readonly System.Drawing.Icon? _redIcon;
        private readonly System.Drawing.Icon? _grayIcon;
        private readonly System.Drawing.Icon? _purpleIcon;
        private TimeSpan? _lastTimeRemaining;
        private bool _isExplicitExit;
        private ToolStripMenuItem? _pauseTrayMenuItem;
        private ToolStripMenuItem? _splitTunnelTrayMenuItem;
        private ToolStripMenuItem? _fullTunnelTrayMenuItem;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // Load custom WireFox icons (base and status borders)
            _defaultIcon = LoadAppIcon() ?? SystemIcons.Shield;
            _greenIcon = LoadIconNamed("wirefox_green.ico") ?? _defaultIcon;
            _orangeIcon = LoadIconNamed("wirefox_orange.ico") ?? _defaultIcon;
            _redIcon = LoadIconNamed("wirefox_red.ico") ?? _defaultIcon;
            _grayIcon = LoadIconNamed("wirefox_gray.ico") ?? _defaultIcon;
            _purpleIcon = LoadIconNamed("wirefox_purple.ico") ?? _defaultIcon;

            try
            {
                // Set WPF window icon
                using var ms = new MemoryStream();
                _defaultIcon.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(ms, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                Icon = decoder.Frames[0];
            }
            catch { }

            // Setup tray icon
            _trayIcon = new NotifyIcon
            {
                Text = "WireFox - WireGuard Roaming",
                Icon = _defaultIcon,
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open WireFox", null, (s, e) => ShowAndRestore());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Kill WireGuard (Force Stop)", null, async (s, e) => await WatchdogService.Instance.KillAllWireGuardAsync());
            contextMenu.Items.Add(new ToolStripSeparator());
            _pauseTrayMenuItem = new ToolStripMenuItem("Pause VPN (15 Mins)", null, async (s, e) => await OnTrayPauseToggleClicked());
            contextMenu.Items.Add(_pauseTrayMenuItem);
            _splitTunnelTrayMenuItem = new ToolStripMenuItem("Split-Tunnel (15 Mins)", null, async (s, e) => await WatchdogService.Instance.EnableSplitTunnelAsync(TimeSpan.FromMinutes(15)));
            contextMenu.Items.Add(_splitTunnelTrayMenuItem);
            _fullTunnelTrayMenuItem = new ToolStripMenuItem("Restart Tunnel", null, async (s, e) => await OnTrayFullTunnelActionClicked());
            contextMenu.Items.Add(_fullTunnelTrayMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) => ShowAndRestore();
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowAndRestore();
                }
            };

            // Hook reactive status events for tray icon & tooltip updates
            WatchdogService.Instance.StatusChanged += OnWatchdogStatusChanged;
            WatchdogService.Instance.SessionTimeRemainingUpdated += OnSessionTimeRemainingUpdated;
            NetworkMonitorService.Instance.NetworkSettled += OnNetworkSettled;
            ConfigManager.Instance.ConfigChanged += OnConfigChanged;

            // Initial visual update
            UpdateTrayVisuals();

            Loaded += OnWindowLoaded;

            bool isInitial = ConfigManager.Instance.IsFirstRun || !ConfigManager.Instance.Config.InitialSetupCompleted;
            if (ConfigManager.Instance.Config.StartMinimized && !isInitial)
            {
                ShowInTaskbar = false;
                WindowState = WindowState.Minimized;
            }
        }

        private void OnWatchdogStatusChanged(TunnelStatus status) => Dispatcher.InvokeAsync(UpdateTrayVisuals);
        private void OnSessionTimeRemainingUpdated(TimeSpan? remaining)
        {
            _lastTimeRemaining = remaining;
            Dispatcher.InvokeAsync(UpdateTrayVisuals);
        }
        private void OnNetworkSettled(NetworkState state) => Dispatcher.InvokeAsync(UpdateTrayVisuals);
        private void OnConfigChanged(AppConfig cfg) => Dispatcher.InvokeAsync(UpdateTrayVisuals);

        private void UpdateTrayVisuals()
        {
            try
            {
                var status = WatchdogService.Instance.Status;
                var netState = NetworkMonitorService.Instance.CurrentState;
                string currentTunnel = ConfigManager.Instance.Config.WireGuardInterface ?? "wg0";

                System.Drawing.Icon targetIcon;
                string statusSummary;

                switch (status)
                {
                    case TunnelStatus.Connected:
                        targetIcon = _greenIcon ?? _defaultIcon;
                        statusSummary = $"Protected ({currentTunnel})";
                        break;

                    case TunnelStatus.Bypassed:
                        targetIcon = _defaultIcon; // Signature Teal Shield for Trusted Network
                        string networkName = !string.IsNullOrEmpty(netState.Ssid) ? netState.Ssid : "Trusted LAN";
                        statusSummary = $"Trusted ({networkName})";
                        break;

                    case TunnelStatus.Paused:
                        targetIcon = _orangeIcon ?? _defaultIcon;
                        if (_lastTimeRemaining.HasValue && _lastTimeRemaining.Value.TotalSeconds > 0)
                        {
                            int mins = Math.Max(1, (int)Math.Ceiling(_lastTimeRemaining.Value.TotalMinutes));
                            statusSummary = $"Paused ({mins}m remaining)";
                        }
                        else
                        {
                            statusSummary = "Paused";
                        }
                        break;

                    case TunnelStatus.SplitTunnel:
                        targetIcon = _orangeIcon ?? _defaultIcon;
                        if (_lastTimeRemaining.HasValue && _lastTimeRemaining.Value.TotalSeconds > 0)
                        {
                            int mins = Math.Max(1, (int)Math.Ceiling(_lastTimeRemaining.Value.TotalMinutes));
                            statusSummary = $"Split-Tunnel ({mins}m remaining)";
                        }
                        else
                        {
                            statusSummary = "Split-Tunnel Active";
                        }
                        break;

                    case TunnelStatus.Connecting:
                        targetIcon = _orangeIcon ?? _defaultIcon;
                        statusSummary = "Connecting...";
                        break;

                    case TunnelStatus.Disconnected:
                    default:
                        if (netState.IsConnected && !netState.IsTrusted)
                        {
                            targetIcon = _redIcon ?? _defaultIcon; // Problem: Untrusted public Wi-Fi with tunnel down!
                            string untrustedNet = !string.IsNullOrEmpty(netState.Ssid) ? netState.Ssid : "Public Network";
                            statusSummary = $"Unprotected ({untrustedNet})";
                        }
                        else if (WatchdogService.Instance.IsManuallyDisabled)
                        {
                            targetIcon = _grayIcon ?? _defaultIcon; // Colorless: Manually disabled by user
                            statusSummary = "Disabled";
                        }
                        else if (!netState.IsConnected)
                        {
                            targetIcon = _grayIcon ?? _defaultIcon; // Colorless: Offline / Airplane Mode
                            statusSummary = "Offline";
                        }
                        else
                        {
                            targetIcon = _grayIcon ?? _defaultIcon; // Colorless: Inactive
                            statusSummary = "Disconnected";
                        }
                        break;
                }

                string tooltip = $"WireFox - {statusSummary}";
                // Win32 NotifyIcon.Text max length is 63 characters
                if (tooltip.Length > 63)
                {
                    tooltip = tooltip.Substring(0, 60) + "...";
                }

                if (!ReferenceEquals(_trayIcon.Icon, targetIcon))
                {
                    _trayIcon.Icon = targetIcon;
                }
                _trayIcon.Text = tooltip;

                if (_pauseTrayMenuItem != null)
                {
                    if (status == TunnelStatus.Paused)
                    {
                        _pauseTrayMenuItem.Text = "Resume VPN (Unpause)";
                        _pauseTrayMenuItem.Enabled = true;
                    }
                    else if (status == TunnelStatus.Connected || status == TunnelStatus.SplitTunnel)
                    {
                        _pauseTrayMenuItem.Text = "Pause VPN (15 Mins)";
                        _pauseTrayMenuItem.Enabled = true;
                    }
                    else
                    {
                        _pauseTrayMenuItem.Text = "Pause VPN (15 Mins)";
                        _pauseTrayMenuItem.Enabled = false;
                    }
                }

                var config = ConfigManager.Instance.Config;
                string effInterface = !string.IsNullOrEmpty(config.WireGuardInterface) ? config.WireGuardInterface : "wg0";
                var profile = config.GetProfile(effInterface);
                bool hasSplitTunnel = !string.IsNullOrWhiteSpace(profile.SplitTunnelAllowedIPs);

                if (_splitTunnelTrayMenuItem != null)
                {
                    _splitTunnelTrayMenuItem.Enabled = hasSplitTunnel && status == TunnelStatus.Connected;
                }

                if (_fullTunnelTrayMenuItem != null)
                {
                    if (status == TunnelStatus.SplitTunnel)
                    {
                        _fullTunnelTrayMenuItem.Text = "Restore Full Tunnel";
                        _fullTunnelTrayMenuItem.Enabled = true;
                    }
                    else if (status == TunnelStatus.Connected)
                    {
                        _fullTunnelTrayMenuItem.Text = "Restart Tunnel";
                        _fullTunnelTrayMenuItem.Enabled = true;
                    }
                    else
                    {
                        _fullTunnelTrayMenuItem.Text = "Restart Tunnel";
                        _fullTunnelTrayMenuItem.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("MainWindow", $"Failed to update tray visuals: {ex.Message}");
            }
        }

        private async Task OnTrayPauseToggleClicked()
        {
            if (WatchdogService.Instance.Status == TunnelStatus.Paused)
            {
                await WatchdogService.Instance.RevertToFullTunnelAsync();
            }
            else
            {
                await WatchdogService.Instance.PauseTunnelAsync(TimeSpan.FromMinutes(15));
            }
        }

        private async Task OnTrayFullTunnelActionClicked()
        {
            if (WatchdogService.Instance.Status == TunnelStatus.SplitTunnel)
            {
                await WatchdogService.Instance.RevertToFullTunnelAsync();
            }
            else
            {
                await WatchdogService.Instance.RestartTunnelAsync();
            }
        }

        private static System.Drawing.Icon? LoadIconNamed(string iconName)
        {
            try
            {
                var asm = typeof(MainWindow).Assembly;
                // Assembly embedded resource name format: WireFox.Assets.<filename> or WireFox.<filename>
                using var resStream = asm.GetManifestResourceStream($"WireFox.Assets.{iconName}")
                                   ?? asm.GetManifestResourceStream($"WireFox.{iconName}");
                if (resStream != null)
                {
                    return new System.Drawing.Icon(resStream);
                }
            }
            catch { }

            try
            {
                string assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", iconName);
                if (File.Exists(assetsPath)) return new System.Drawing.Icon(assetsPath);

                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconName);
                if (File.Exists(iconPath)) return new System.Drawing.Icon(iconPath);

                string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(exeDir))
                {
                    string candidateAssets = Path.Combine(exeDir, "Assets", iconName);
                    if (File.Exists(candidateAssets)) return new System.Drawing.Icon(candidateAssets);

                    string candidate = Path.Combine(exeDir, iconName);
                    if (File.Exists(candidate)) return new System.Drawing.Icon(candidate);
                }
            }
            catch { }
            return null;
        }

        private static System.Drawing.Icon? LoadAppIcon()
        {
            try
            {
                var embedded = LoadIconNamed("wirefox.ico");
                if (embedded != null) return embedded;

                var extracted = Environment.ProcessPath != null ? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath) : null;
                if (extracted != null) return extracted;

                return LoadIconNamed("wgm.ico");
            }
            catch { }

            return null;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            _trayIcon.Visible = true;

            // Register page instances
            RootNavigation.SetServiceProvider(new SimpleServiceProvider(
                new StatusPage(_viewModel.StatusVM),
                new TrustedNetworksPage(_viewModel.TrustedNetworksVM),
                new DiagnosticsPage(_viewModel.DiagnosticsVM),
                new SettingsPage(_viewModel.SettingsVM)
            ));

            _viewModel.SettingsVM.InitialSetupFinished += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    RootNavigation.Navigate(typeof(StatusPage));
                });
            };

            bool isInitialSetup = ConfigManager.Instance.IsFirstRun || !ConfigManager.Instance.Config.InitialSetupCompleted;

            if (isInitialSetup)
            {
                // When %appdata% is missing or wiped, drop user directly into configuration
                RootNavigation.Navigate(typeof(SettingsPage));
                ShowAndRestore();
            }
            else
            {
                // Navigate to default page
                RootNavigation.Navigate(typeof(StatusPage));

                if (ConfigManager.Instance.Config.StartMinimized)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
                else
                {
                    ShowInTaskbar = true;
                    Show();
                    Activate();
                }
            }
        }

        private void CleanupSubscriptions()
        {
            WatchdogService.Instance.StatusChanged -= OnWatchdogStatusChanged;
            WatchdogService.Instance.SessionTimeRemainingUpdated -= OnSessionTimeRemainingUpdated;
            NetworkMonitorService.Instance.NetworkSettled -= OnNetworkSettled;
            ConfigManager.Instance.ConfigChanged -= OnConfigChanged;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                CleanupSubscriptions();
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                base.OnClosing(e);
            }
        }

        public void ShowAndRestore()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    ShowInTaskbar = true;
                    if (!IsVisible)
                    {
                        Show();
                    }
                    Visibility = Visibility.Visible;
                    if (WindowState == WindowState.Minimized)
                    {
                        WindowState = WindowState.Normal;
                    }
                    Activate();
                    Topmost = true;
                    Topmost = false;
                    Focus();
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Debug("MainWindow", $"ShowAndRestore error: {ex.Message}");
                }
            });
        }

        public void NavigateToSettings()
        {
            try
            {
                RootNavigation.Navigate(typeof(SettingsPage));
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("MainWindow", "Failed to navigate to settings", ex);
            }
        }

        private void ExitApplication()
        {
            _isExplicitExit = true;
            CleanupSubscriptions();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Close();
            System.Windows.Application.Current.Shutdown();
        }

        private class SimpleServiceProvider : IServiceProvider
        {
            private readonly StatusPage _statusPage;
            private readonly TrustedNetworksPage _trustedNetworksPage;
            private readonly DiagnosticsPage _diagnosticsPage;
            private readonly SettingsPage _settingsPage;

            public SimpleServiceProvider(StatusPage statusPage, TrustedNetworksPage trustedNetworksPage, DiagnosticsPage diagnosticsPage, SettingsPage settingsPage)
            {
                _statusPage = statusPage;
                _trustedNetworksPage = trustedNetworksPage;
                _diagnosticsPage = diagnosticsPage;
                _settingsPage = settingsPage;
            }

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(StatusPage)) return _statusPage;
                if (serviceType == typeof(TrustedNetworksPage)) return _trustedNetworksPage;
                if (serviceType == typeof(DiagnosticsPage)) return _diagnosticsPage;
                if (serviceType == typeof(SettingsPage)) return _settingsPage;
                return null;
            }
        }
    }
}
