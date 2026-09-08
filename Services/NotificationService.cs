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

        public void ShowNewNetworkPrompt(string ssid, bool isVpnActive)
        {
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

                builder.Show();
                LoggingService.Instance.Info("NotificationService", $"ShowNewNetworkPrompt displayed for '{ssid}', VpnActive={isVpnActive}");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowNewNetworkPrompt error", ex);
            }
        }

        public void ShowWatchdogBlockedPrompt(string interfaceName)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("VPN Tunnel Blocked")
                    .AddText($"No handshake received on {interfaceName}. Captive portal or firewall may be blocking UDP.")
                    .AddButton(new ToastButton()
                        .SetContent("Pause 15 Mins")
                        .AddArgument("action", "pause")
                        .AddArgument("param", "15")
                        .SetBackgroundActivation())
                    .AddButton(new ToastButton()
                        .SetContent("Restart Tunnel")
                        .AddArgument("action", "restart_tunnel").AddArgument("param", "1")
                        .SetBackgroundActivation())
                    .Show();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowWatchdogBlockedPrompt error", ex);
            }
        }

        public void ShowTimerExpiringWarning(string sessionType, int minutesRemaining)
        {
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
                    .Show();
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
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowNotification error", ex);
            }
        }

        public void ShowUpdatePrompt(string newVersion, string releaseNotesUrl)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText("🦊 WireFox Update Available")
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
                    .Show();

                LoggingService.Instance.Info("NotificationService", $"ShowUpdatePrompt displayed for '{newVersion}'");
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NotificationService", "ShowUpdatePrompt error", ex);
            }
        }
    }
}
