using System;
using CommunityToolkit.WinUI.Notifications;

namespace WireFox.Services
{
    public class NotificationService
    {
        private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
        public static NotificationService Instance => _instance.Value;

        public event Action<string, string?>? ActionTriggered;

        public NotificationService()
        {
            try
            {
                ToastNotificationManagerCompat.OnActivated += toastArgs =>
                {
                    var args = ToastArguments.Parse(toastArgs.Argument);
                    if (args.TryGetValue("action", out string? action))
                    {
                        args.TryGetValue("param", out string? param);
                        LoggingService.Instance.Info("NotificationService", $"Toast action clicked: action={action}, param={param}");
                        ActionTriggered?.Invoke(action, param);
                    }
                };
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "Init error", ex);
            }
        }

        private readonly System.Collections.Generic.Dictionary<string, DateTime> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public bool CheckAndSetCooldown(string key, TimeSpan cooldown)
        {
            lock (_lock)
            {
                if (_cooldowns.TryGetValue(key, out var lastShown))
                {
                    if (DateTime.UtcNow - lastShown < cooldown)
                    {
                        LoggingService.Instance.Debug("NotificationService", $"Suppressed toast '{key}' due to active cooldown.");
                        return false;
                    }
                }
                _cooldowns[key] = DateTime.UtcNow;
                return true;
            }
        }

        public void ResetCooldown(string key)
        {
            lock (_lock)
            {
                _cooldowns.Remove(key);
            }
        }

        public void ShowNewNetworkPrompt(string ssid, bool isVpnActive)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;
            if (!CheckAndSetCooldown($"new_network_{ssid}", TimeSpan.FromMinutes(10))) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText("New Network Detected")
                    .AddText(isVpnActive 
                        ? $"Connected to '{ssid}'. VPN is currently active." 
                        : $"Connected to '{ssid}'. VPN is currently disconnected.");

                builder.AddButton(new ToastButton()
                    .SetContent("Trust & Bypass VPN")
                    .AddArgument("action", "trust_ssid")
                    .AddArgument("param", ssid)
                    .SetBackgroundActivation());

                if (isVpnActive)
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("Keep VPN Active")
                        .AddArgument("action", "keep_vpn")
                        .AddArgument("param", ssid)
                        .SetBackgroundActivation());
                }
                else
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("Turn On VPN")
                        .AddArgument("action", "enable_vpn")
                        .AddArgument("param", ssid)
                        .SetBackgroundActivation());
                }

                builder.Show(toast =>
                {
                    toast.Tag = $"net_{ssid}";
                    toast.Group = "wirefox";
                });
                LoggingService.Instance.Info("NotificationService", $"ShowNewNetworkPrompt displayed for '{ssid}', VpnActive={isVpnActive}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowNewNetworkPrompt error", ex);
            }
        }

        public void ShowWatchdogBlockedPrompt(string interfaceName)
        {
            if (!CheckAndSetCooldown($"watchdog_blocked_{interfaceName}", TimeSpan.FromMinutes(3))) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("VPN Tunnel Blocked")
                    .AddText($"No handshake or internet response on {interfaceName}. Captive portal, firewall, or driver freeze detected.")
                    .AddButton(new ToastButton()
                        .SetContent("Pause 15 Mins")
                        .AddArgument("action", "pause")
                        .AddArgument("param", "15")
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("Restart Tunnel")
                        .AddArgument("action", "restart_tunnel").AddArgument("param", "1")
                        .SetBackgroundActivation())
                    .Show(toast =>
                    {
                        toast.Tag = "watchdog_blocked";
                        toast.Group = "wirefox";
                    });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowWatchdogBlockedPrompt error", ex);
            }
        }

        public void ShowTimerExpiringWarning(string sessionType, int minutesRemaining)
        {
            if (!CheckAndSetCooldown("timer_expiring_warning", TimeSpan.FromMinutes(2))) return;

            try
            {
                new ToastContentBuilder()
                    .AddText($"VPN {sessionType} Expiring Soon")
                    .AddText($"Full tunnel will re-enable in {minutesRemaining} minutes.")
                    .AddButton(new ToastButton()
                        .SetContent("+15 Mins")
                        .AddArgument("action", "extend_pause")
                        .AddArgument("param", "15")
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("Revert to Full Tunnel")
                        .AddArgument("action", "revert_full").AddArgument("param", "1")
                        .SetBackgroundActivation())
                    .Show(toast =>
                    {
                        toast.Tag = "timer_expiring";
                        toast.Group = "wirefox";
                    });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowTimerExpiringWarning error", ex);
            }
        }

        public void ShowNotification(string title, string message)
        {
            try
            {
                string tag = $"wirefox_notif_{title.ToLowerInvariant().Replace(' ', '_')}";
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show(toast =>
                    {
                        toast.Tag = tag;
                        toast.Group = "wirefox";
                    });
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowNotification error", ex);
            }
        }

        public void ShowUpdatePrompt(string newVersion, string releaseNotesUrl)
        {
            if (!CheckAndSetCooldown($"update_{newVersion}", TimeSpan.FromHours(1))) return;

            try
            {
                new ToastContentBuilder()
                    .AddText("\U0001F98A WireFox Update Available")
                    .AddText($"A new release ({newVersion}) is available on GitHub.")
                    .AddButton(new ToastButton()
                        .SetContent("Update Now")
                        .AddArgument("action", "update_now")
                        .AddArgument("param", newVersion)
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("View in Settings")
                        .AddArgument("action", "open_settings")
                        .AddArgument("param", newVersion)
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("Skip Version")
                        .AddArgument("action", "skip_update_version")
                        .AddArgument("param", newVersion)
                        .SetBackgroundActivation())
                    .Show(toast =>
                    {
                        toast.Tag = "update_prompt";
                        toast.Group = "wirefox";
                    });

                LoggingService.Instance.Info("NotificationService", $"ShowUpdatePrompt displayed for '{newVersion}'");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowUpdatePrompt error", ex);
            }
        }
    }
}
