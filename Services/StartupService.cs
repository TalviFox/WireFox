using System;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace WireFox.Services
{
    public static class StartupService
    {
        public const string TaskName = "WireFox";
        public const string LegacyTaskName = "WireGuardManager";

        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegistryValueName = "WireFox";
        private const string LegacyRunRegistryValueName = "WireGuardManager";

        /// <summary>
        /// Checks if WireFox is configured to start with Windows,
        /// either via the elevated Scheduled Task (preferred) or legacy registry key.
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                using var ts = new TaskService();
                var task = ts.FindTask(TaskName);
                if (task != null)
                {
                    return task.Enabled;
                }

                // Check legacy task if WireFox task hasn't been registered yet
                var legacyTask = ts.FindTask(LegacyTaskName);
                if (legacyTask != null)
                {
                    return legacyTask.Enabled;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Startup", $"Failed to query scheduled task status: {ex.Message}");
            }

            // Fallback check for legacy registry key
            return CheckLegacyRegistry();
        }

        /// <summary>
        /// Enables or disables starting with Windows via a scheduled task.
        /// When enabled, creates an elevated Task Scheduler entry with InteractiveToken.
        /// When disabled, deletes the task completely.
        /// Also ensures legacy registry run keys are cleaned up.
        /// </summary>
        public static bool SetStartup(bool enable)
        {
            // Always purge legacy registry key so it never conflicts or misleads
            RemoveLegacyRegistry();

            try
            {
                using var ts = new TaskService();

                // Clean up any legacy WireGuardManager scheduled task
                try
                {
                    var oldTask = ts.FindTask(LegacyTaskName);
                    if (oldTask != null)
                    {
                        ts.RootFolder.DeleteTask(LegacyTaskName, false);
                        LoggingService.Instance.Info("Startup", $"Cleaned up legacy scheduled task '{LegacyTaskName}'.");
                    }
                }
                catch { }

                if (enable)
                {
                    string? exePath = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    }

                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                    {
                        LoggingService.Instance.Error("Startup", "Cannot configure startup: executable path not found.");
                        return false;
                    }

                    string workingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;

                    var td = ts.NewTask();
                    td.RegistrationInfo.Description = "WireFox - Auto-start on user logon with elevated privileges (managed via WireFox Settings)";
                    td.RegistrationInfo.Author = "FoxDen Software";

                    // Trigger: at user logon
                    var currentIdentity = WindowsIdentity.GetCurrent().Name;
                    var logonTrigger = new LogonTrigger
                    {
                        UserId = currentIdentity,
                        Delay = TimeSpan.FromSeconds(2) // 2-second buffer for desktop shell to stabilize
                    };
                    td.Triggers.Add(logonTrigger);

                    // Principal: Highest privileges + Interactive session (shows UI/tray)
                    td.Principal.RunLevel = TaskRunLevel.Highest;
                    td.Principal.LogonType = TaskLogonType.InteractiveToken;

                    // Power & execution constraints:
                    // Critical for laptops: allow running on battery power
                    td.Settings.DisallowStartIfOnBatteries = false;
                    td.Settings.StopIfGoingOnBatteries = false;
                    // Infinite execution limit (don't kill background VPN manager after 72 hours)
                    td.Settings.ExecutionTimeLimit = TimeSpan.Zero;
                    td.Settings.AllowDemandStart = true;
                    td.Settings.StartWhenAvailable = true;
                    td.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;

                    // Action
                    td.Actions.Add(new ExecAction(exePath, null, workingDirectory));

                    // Register or overwrite
                    ts.RootFolder.RegisterTaskDefinition(
                        TaskName,
                        td,
                        TaskCreation.CreateOrUpdate,
                        null,
                        null,
                        TaskLogonType.InteractiveToken
                    );

                    LoggingService.Instance.Success("Startup", $"Scheduled task '{TaskName}' registered successfully for logon as {currentIdentity} with elevated privileges.");
                    return true;
                }
                else
                {
                    var existingTask = ts.FindTask(TaskName);
                    if (existingTask != null)
                    {
                        ts.RootFolder.DeleteTask(TaskName, false);
                        LoggingService.Instance.Info("Startup", $"Scheduled task '{TaskName}' removed successfully.");
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Startup", $"Failed to update Windows startup scheduled task: {ex.Message}", ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Synchronizes startup state on app launch. If the user previously selected
        /// 'Start with Windows' or had a legacy registry entry, migrates or refreshes
        /// the scheduled task with the current executable path.
        /// </summary>
        public static void SyncStartupState(bool desiredEnabled)
        {
            try
            {
                bool legacyPresent = CheckLegacyRegistry();

                if (desiredEnabled)
                {
                    string? currentExePath = Environment.ProcessPath;
                    bool needsRegistration = true;

                    using (var ts = new TaskService())
                    {
                        var task = ts.FindTask(TaskName);
                        if (task != null && task.Enabled)
                        {
                            // Check if current action path matches this exe
                            if (task.Definition.Actions.Count > 0 &&
                                task.Definition.Actions[0] is ExecAction execAction)
                            {
                                if (string.Equals(execAction.Path, currentExePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    needsRegistration = false;
                                }
                            }
                        }
                    }

                    if (needsRegistration || legacyPresent)
                    {
                        LoggingService.Instance.Info("Startup", "Synchronizing/updating startup scheduled task with current executable path...");
                        SetStartup(true);
                    }
                }
                else
                {
                    // Ensure disabled
                    if (legacyPresent)
                    {
                        RemoveLegacyRegistry();
                    }

                    using var ts = new TaskService();
                    var task = ts.FindTask(TaskName);
                    if (task != null)
                    {
                        ts.RootFolder.DeleteTask(TaskName, false);
                        LoggingService.Instance.Info("Startup", $"Removed obsolete startup task '{TaskName}'.");
                    }

                    var oldTask = ts.FindTask(LegacyTaskName);
                    if (oldTask != null)
                    {
                        ts.RootFolder.DeleteTask(LegacyTaskName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Startup", $"Error while synchronizing startup state: {ex.Message}");
            }
        }

        private static bool CheckLegacyRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
                return key?.GetValue(RunRegistryValueName) != null || key?.GetValue(LegacyRunRegistryValueName) != null;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveLegacyRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key != null)
                {
                    if (key.GetValue(RunRegistryValueName) != null)
                    {
                        key.DeleteValue(RunRegistryValueName, false);
                    }
                    if (key.GetValue(LegacyRunRegistryValueName) != null)
                    {
                        key.DeleteValue(LegacyRunRegistryValueName, false);
                    }
                    LoggingService.Instance.Info("Startup", "Cleaned up legacy registry run entry.");
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Warning("Startup", $"Could not clean up legacy registry entry: {ex.Message}");
            }
        }
    }
}
