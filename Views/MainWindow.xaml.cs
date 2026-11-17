using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
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
        private AboutWindow? _aboutWindow;

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
            _pauseTrayMenuItem = new ToolStripMenuItem("Pause VPN (15 Mins)", null, (s, e) => _ = Task.Run(async () => await OnTrayPauseToggleClicked()));
            contextMenu.Items.Add(_pauseTrayMenuItem);
            _splitTunnelTrayMenuItem = new ToolStripMenuItem("Split-Tunnel (15 Mins)", null, (s, e) => _ = Task.Run(async () => await WatchdogService.Instance.EnableSplitTunnelAsync(TimeSpan.FromMinutes(15))));
            contextMenu.Items.Add(_splitTunnelTrayMenuItem);
            _fullTunnelTrayMenuItem = new ToolStripMenuItem("Restart Tunnel", null, (s, e) => _ = Task.Run(async () => await OnTrayFullTunnelActionClicked()));
            contextMenu.Items.Add(_fullTunnelTrayMenuItem);
            contextMenu.Items.Add(new ToolStripSeparator());

            var restartMenu = new ToolStripMenuItem("Restart");
            restartMenu.DropDownItems.Add("Restart WireFox", null, (s, e) => _ = RestartApplicationAsync(restartWireGuard: false));
            restartMenu.DropDownItems.Add("Restart WireFox + WireGuard", null, (s, e) => _ = RestartApplicationAsync(restartWireGuard: true));
            contextMenu.Items.Add(restartMenu);

            var aboutMenu = new ToolStripMenuItem("About");
            aboutMenu.DropDownItems.Add("About WireFox", null, (s, e) => ShowAboutWindow());
            aboutMenu.DropDownItems.Add("GitHub Repository", null, (s, e) => OpenUrl("https://github.com/TalviFox/WireFox"));
            contextMenu.Items.Add(aboutMenu);

            contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

            contextMenu.Opened += (s, e) => EnsureMenuAboveTaskbar(contextMenu, clampToWorkingArea: true);
            restartMenu.DropDown.Opened += (s, e) => EnsureMenuAboveTaskbar(restartMenu.DropDown, clampToWorkingArea: false);
            aboutMenu.DropDown.Opened += (s, e) => EnsureMenuAboveTaskbar(aboutMenu.DropDown, clampToWorkingArea: false);

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
                        if (netState.IsEvaluated && !netState.IsConnected)
                        {
                            targetIcon = _grayIcon ?? _defaultIcon;
                            statusSummary = "Tunnel Ready (Offline)";
                        }
                        else
                        {
                            targetIcon = _greenIcon ?? _defaultIcon;
                            statusSummary = $"Protected ({currentTunnel})";
                        }
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
            if (!_isExplicitExit && !App.IsSessionEnding)
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

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private uint _taskbarCreatedMsg;

        private static void EnsureMenuAboveTaskbar(ToolStripDropDown menu, bool clampToWorkingArea)
        {
            try
            {
                if (clampToWorkingArea)
                {
                    var workingArea = Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
                    int left = menu.Left;
                    int top = menu.Top;

                    if (menu.Bottom > workingArea.Bottom - 4)
                        top = Math.Max(workingArea.Top + 4, workingArea.Bottom - menu.Height - 4);
                    if (top < workingArea.Top + 4)
                        top = workingArea.Top + 4;

                    if (menu.Right > workingArea.Right - 4)
                        left = Math.Max(workingArea.Left + 4, workingArea.Right - menu.Width - 4);
                    if (left < workingArea.Left + 4)
                        left = workingArea.Left + 4;

                    if (left != menu.Left || top != menu.Top)
                    {
                        menu.Location = new System.Drawing.Point(left, top);
                    }
                }

                if (menu.IsHandleCreated && menu.Handle != IntPtr.Zero)
                {
                    SetWindowPos(menu.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd != IntPtr.Zero)
                {
                    _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
                    var source = HwndSource.FromHwnd(hwnd);
                    source?.AddHook(HwndHook);
                }
            }
            catch { }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_taskbarCreatedMsg != 0 && msg == (int)_taskbarCreatedMsg)
            {
                // Explorer restarted or shell finished initialization - revive tray icon
                try
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Visible = true;
                    UpdateTrayVisuals();
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        public void ShowAndRestore()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    ShowInTaskbar = true;

                    // Ensure HWND is initialized even if launched minimized to tray
                    var helper = new WindowInteropHelper(this);
                    IntPtr hwnd = helper.EnsureHandle();

                    Visibility = Visibility.Visible;
                    if (WindowState == WindowState.Minimized || (hwnd != IntPtr.Zero && IsIconic(hwnd)))
                    {
                        WindowState = WindowState.Normal;
                    }

                    if (!IsVisible)
                    {
                        Show();
                    }

                    // Native Win32 window restoration to reliably bypass Windows 11 foreground suppression
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindowAsync(hwnd, SW_RESTORE);
                        BringWindowToTop(hwnd);
                        SetForegroundWindow(hwnd);
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

        private void ShowAboutWindow()
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_aboutWindow != null && _aboutWindow.IsLoaded)
                    {
                        if (_aboutWindow.WindowState == WindowState.Minimized)
                        {
                            _aboutWindow.WindowState = WindowState.Normal;
                        }
                        _aboutWindow.Activate();
                        _aboutWindow.Topmost = true;
                        _aboutWindow.Topmost = false;
                        return;
                    }

                    _aboutWindow = new AboutWindow(_viewModel.SettingsVM)
                    {
                        Icon = Icon
                    };
                    _aboutWindow.Closed += (_, _) => _aboutWindow = null;
                    _aboutWindow.Show();
                    _aboutWindow.Activate();
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Debug("MainWindow", $"ShowAboutWindow error: {ex.Message}");
                }
            });
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("MainWindow", $"OpenUrl error: {ex.Message}");
            }
        }

        private async Task RestartApplicationAsync(bool restartWireGuard)
        {
            try
            {
                if (restartWireGuard)
                {
                    await Task.Run(async () => await WatchdogService.Instance.ForceRestartTunnelAsync());
                }

                await Dispatcher.InvokeAsync(() =>
                {
                    _isExplicitExit = true;
                    CleanupSubscriptions();
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    WatchdogService.Instance.Stop();
                    App.ReleaseSingleInstanceResources();

                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = exePath,
                            UseShellExecute = true
                        });
                    }

                    Close();
                    System.Windows.Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("MainWindow", "Failed to restart WireFox", ex);
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
