using System;
using System.Diagnostics;
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
        /// Indicates whether the current executable is running from a protected Program Files directory.
        /// </summary>
        public static bool IsRunningFromProgramFiles
        {
            get
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (string.IsNullOrEmpty(exePath)) return false;

                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string pfX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

                return exePath.StartsWith(pf, StringComparison.OrdinalIgnoreCase) ||
                       exePath.StartsWith(pfX86, StringComparison.OrdinalIgnoreCase);
            }
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
                    if (!IsRunningFromProgramFiles)
                    {
                        LoggingService.Instance.Warning("Startup", "Automatic elevated startup is disabled in portable mode to prevent privilege escalation. Install WireFox to Program Files first.");
                        return false;
                    }

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
        /// Synchronizes startup state on app launch. If running portably outside Program Files,
        /// scheduled task registration is skipped to protect system security.
        /// </summary>
        public static void SyncStartupState(bool desiredEnabled)
        {
            try
            {
                bool legacyPresent = CheckLegacyRegistry();

                if (!IsRunningFromProgramFiles)
                {
                    if (desiredEnabled)
                    {
                        LoggingService.Instance.Info("Startup", "Portable mode detected: skipping automatic scheduled task registration.");
                    }

                    // Ensure any existing task is purged if running portably
                    try
                    {
                        using var ts = new TaskService();
                        var task = ts.FindTask(TaskName);
                        if (task != null) ts.RootFolder.DeleteTask(TaskName, false);
                        var oldTask = ts.FindTask(LegacyTaskName);
                        if (oldTask != null) ts.RootFolder.DeleteTask(LegacyTaskName, false);
                    }
                    catch { }

                    if (legacyPresent) RemoveLegacyRegistry();
                    return;
                }

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

        /// <summary>
        /// Seamlessly installs the running portable executable into Program Files\WireFox,
        /// creates the Start Menu shortcut, registers Windows Installed Apps entry,
        /// and launches the installed application.
        /// </summary>
        public static bool InstallToProgramFiles()
        {
            try
            {
                string? currentExe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExe))
                {
                    currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe)) return false;

                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireFox");
                string targetExe = Path.Combine(targetDir, "WireFox.exe");
                string uninstallerTarget = Path.Combine(targetDir, "uninstall.ps1");
                string version = UpdateService.Instance.GetCurrentVersionString();
                string sourceDir = Path.GetDirectoryName(currentExe) ?? "";

                // Staging directory for the elevated helper script
                string tempDir = Path.Combine(Path.GetTempPath(), "WireFox_Installer");
                Directory.CreateDirectory(tempDir);
                string installScript = Path.Combine(tempDir, "install_to_program_files.ps1");

                string scriptContent = $@"
# WireFox Elevated Program Files Installer
Add-Type -AssemblyName System.Windows.Forms
Start-Sleep -Milliseconds 300

# Stop any running WireFox instances except ourselves
Get-Process -Name 'WireFox' -ErrorAction SilentlyContinue | Where-Object {{ $_.Id -ne $PID }} | Stop-Process -Force
Start-Sleep -Milliseconds 500

$targetDir = '{targetDir.Replace("'", "''")}'
$targetExe = '{targetExe.Replace("'", "''")}'
$currentExe = '{currentExe.Replace("'", "''")}'
$sourceDir = '{sourceDir.Replace("'", "''")}'

if (-not (Test-Path $targetDir)) {{
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}}

$maxRetries = 10
$retry = 0
$copied = $false
while (-not $copied -and $retry -lt $maxRetries) {{
    try {{
        Copy-Item -Path $currentExe -Destination $targetExe -Force -ErrorAction Stop
        $copied = $true
    }} catch {{
        $retry++
        Start-Sleep -Milliseconds 500
    }}
}}

if (-not $copied) {{
    [System.Windows.Forms.MessageBox]::Show('Failed to copy WireFox to Program Files. The file may be in use.', 'WireFox Installation Error', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}}

# Copy companion scripts if available
$searchDirs = @($sourceDir, (Split-Path -Parent $sourceDir))
foreach ($f in @('uninstall.ps1', 'verify.ps1', 'wirefox.ico')) {{
    foreach ($d in $searchDirs) {{
        $src = Join-Path $d $f
        if (Test-Path $src) {{
            Copy-Item -Path $src -Destination (Join-Path $targetDir $f) -Force -ErrorAction SilentlyContinue
            break
        }}
    }}
}}

# Register in Windows Installed Apps
try {{
    $regKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WireFox'
    if (-not (Test-Path $regKey)) {{ New-Item -Path $regKey -Force | Out-Null }}
    Set-ItemProperty -Path $regKey -Name 'DisplayName' -Value 'WireFox'
    Set-ItemProperty -Path $regKey -Name 'DisplayVersion' -Value '{version.Replace("'", "''")}'
    Set-ItemProperty -Path $regKey -Name 'Publisher' -Value 'FoxDen Software'
    Set-ItemProperty -Path $regKey -Name 'DisplayIcon' -Value ""$targetExe,0""
    Set-ItemProperty -Path $regKey -Name 'InstallLocation' -Value $targetDir
    Set-ItemProperty -Path $regKey -Name 'UninstallString' -Value ""powershell.exe -NoProfile -ExecutionPolicy Bypass -File `""$uninstallerTarget`""""
    Set-ItemProperty -Path $regKey -Name 'URLInfoAbout' -Value 'https://github.com/TalviFox/WireFox'
}} catch {{}}

# Create Start Menu Shortcut
try {{
    $wsh = New-Object -ComObject WScript.Shell
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WireFox.lnk'
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $targetDir
    $shortcut.IconLocation = ""$targetExe,0""
    $shortcut.Description = 'Automated Roaming & Watchdog Manager for WireGuard on Windows'
    $shortcut.Save()
}} catch {{}}

# Inform user and launch newly installed instance
[System.Windows.Forms.MessageBox]::Show('WireFox has been successfully installed to Program Files!', 'WireFox Setup', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Information)
Start-Process -FilePath $targetExe
";
                File.WriteAllText(installScript, scriptContent);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{installScript}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
                LoggingService.Instance.Info("Install", "Launched elevated installer script.");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Install", "InstallToProgramFiles failed", ex);
                return false;
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
