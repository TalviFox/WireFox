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
