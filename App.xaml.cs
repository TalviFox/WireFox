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
            string targetExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireFox", "WireFox.exe");
            bool isInstalled = File.Exists(targetExe);
            bool isRunningFromProgramFiles = StartupService.IsRunningFromProgramFiles;

            _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                // Another instance is already running
                if (!isRunningFromProgramFiles)
                {
                    // Running outside Program Files while an instance is running
                    string text = isInstalled
                        ? "An active instance of WireFox is running from Program Files.\n\nWould you like to close the running instance to update your Program Files installation with this version?"
                        : "An active instance of WireFox is already running on this system.\n\nWould you like to close the running instance to install this version to Program Files?";

                    var choice = System.Windows.MessageBox.Show(
                        text + "\n\n• Click 'Yes' to close the running instance and update/install.\n• Click 'No' to keep the background instance running and exit.",
                        "WireFox", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (choice == MessageBoxResult.Yes)
                    {
                        StartupService.InstallToProgramFiles();
                    }
                }
                else
                {
                    // Running from inside Program Files while an instance is already running
                    var choice = System.Windows.MessageBox.Show(
                        "WireFox is actively monitoring and protecting your WireGuard tunnels in the background.\n\nWould you like to restart the background instance?\n\n• Click 'Yes' to restart WireFox.\n• Click 'No' to keep it running and close this prompt.",
                        "WireFox - Already Running", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (choice == MessageBoxResult.Yes)
                    {
                        try
                        {
                            int currentPid = Environment.ProcessId;
                            foreach (var proc in System.Diagnostics.Process.GetProcessesByName("WireFox"))
                            {
                                if (proc.Id != currentPid)
                                {
                                    proc.Kill();
                                    proc.WaitForExit(3000);
                                }
                            }
                        }
                        catch { }

                        _mutex?.Dispose();
                        _mutex = new Mutex(true, AppMutexName, out _);
                        // Continue startup
                        goto ContinueStartup;
                    }
                }

                Shutdown();
                return;
            }

        ContinueStartup:
            // Global exception logging
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

            base.OnStartup(e);

            // Self-healing: Synchronize Windows Installed Apps DisplayVersion if registered
            SyncInstalledAppsDisplayVersion();

            // Portable Mode Check (Prompt on launch outside Program Files)
            if (!isRunningFromProgramFiles)
            {
                if (isInstalled)
                {
                    // An installation exists in Program Files, but user launched an external exe
                    var choice = System.Windows.MessageBox.Show(
                        $"An existing WireFox installation was detected at:\n{targetExe}\n\nWould you like to update your installation with this version?\n\n• Click 'Yes' to update Program Files.\n• Click 'No' to run this version standalone (Portable Mode).\n• Click 'Cancel' to exit.",
                        "WireFox Setup", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

                    if (choice == MessageBoxResult.Yes)
                    {
                        if (StartupService.InstallToProgramFiles())
                        {
                            Shutdown();
                            return;
                        }
                    }
                    else if (choice != MessageBoxResult.No)
                    {
                        Shutdown();
                        return;
                    }
                }
                else
                {
                    // WireFox is NOT installed in Program Files
                    bool alwaysPortable = ConfigManager.Instance.Config.HasPromptedPortableInstall;
                    if (!alwaysPortable)
                    {
                        var choice = System.Windows.MessageBox.Show(
                            $"WireFox is running from a standalone location:\n{Environment.ProcessPath}\n\nHow would you like to run WireFox?\n\n• Click 'Yes' to Install to Program Files (Recommended - includes Start Menu shortcut and auto-start).\n• Click 'No' to Run in Portable Mode (Runs directly from this folder).\n• Click 'Cancel' to exit.",
                            "WireFox Setup", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

                        if (choice == MessageBoxResult.Yes)
                        {
                            if (StartupService.InstallToProgramFiles())
                            {
                                Shutdown();
                                return;
                            }
                        }
                        else if (choice == MessageBoxResult.No)
                        {
                            // Temporarily don't force 'Always Portable' to prevent accidental suppression,
                            // unless they set it in config directly. They can run it portably without
                            // permanently silencing the installer if they move it.
                        }
                        else
                        {
                            Shutdown();
                            return;
                        }
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
