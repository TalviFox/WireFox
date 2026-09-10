using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WireFox.Services;

namespace WireFox
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private const string AppMutexName = "Global\\WireFox_SingleInstance_Mutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already running
                System.Windows.MessageBox.Show("WireFox is already running in the background.", "WireFox", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Global exception logging
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            base.OnStartup(e);

            // Self-healing: Synchronize Windows Installed Apps DisplayVersion if registered
            SyncInstalledAppsDisplayVersion();

            // Portable Mode Check (Option A: Prompt on first launch outside Program Files)
            if (!StartupService.IsRunningFromProgramFiles && !ConfigManager.Instance.Config.HasPromptedPortableInstall)
            {
                ConfigManager.Instance.Config.HasPromptedPortableInstall = true;
                ConfigManager.Instance.SaveConfig();

                var promptResult = System.Windows.MessageBox.Show(
                    "WireFox detected that it is running in portable mode outside Program Files.\n\n" +
                    "• Click 'Yes' to Install to Program Files (Recommended for 24/7 background roaming and secure auto-start).\n\n" +
                    "• Click 'No' to Continue in Portable Mode (Testing / Disposable system: Auto-start and in-place updates are disabled for security).",
                    "WireFox - Installation Option",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (promptResult == MessageBoxResult.Yes)
                {
                    if (StartupService.InstallToProgramFiles())
                    {
                        Shutdown();
                        return;
                    }
                }
            }

            var theme = ConfigManager.Instance.Config.Theme?.ToLowerInvariant() ?? "system";
            if (theme == "light")
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            else if (theme == "dark")
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
            else
                Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();

            // Synchronize and validate startup scheduled task state
            StartupService.SyncStartupState(ConfigManager.Instance.Config.StartWithWindows);

            // Start background watchdog engine
            WatchdogService.Instance.Start();

            // Delayed background update check (15-second delay to keep startup instantaneous)
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(15));
                await UpdateService.Instance.CheckForUpdatesAsync(isManual: false);
            });
        }

        private static void SyncInstalledAppsDisplayVersion()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WireFox", writable: true);
                if (key != null)
                {
                    string currentVer = UpdateService.Instance.GetCurrentVersionString();
                    string? existingVer = key.GetValue("DisplayVersion") as string;
                    if (!string.Equals(existingVer, currentVer, StringComparison.OrdinalIgnoreCase))
                    {
                        key.SetValue("DisplayVersion", currentVer);
                        LoggingService.Instance.Info("App", $"Synchronized Windows Installed Apps version to {currentVer}.");
                    }
                }
            }
            catch { }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomainUnhandledException", ex);
            }
        }

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
                string logEntry = $"[{DateTime.UtcNow:u}] [{source}] {ex.Message}\n{ex.StackTrace}\n\n";
                File.AppendAllText(logFile, logEntry);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            WatchdogService.Instance.Stop();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
