using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WireFox.Models
{
    public class AppConfig
    {
        [JsonPropertyName("wireguard_interface")]
        public string WireGuardInterface { get; set; } = "wg0";

        [JsonPropertyName("wireguard_config_path")]
        public string? WireGuardConfigPath { get; set; }

        [JsonPropertyName("trusted_networks")]
        public List<string> TrustedNetworks { get; set; } = new();

        [JsonPropertyName("trusted_gateways")]
        public List<TrustedGateway> TrustedGateways { get; set; } = new();

        [JsonPropertyName("known_untrusted_networks")]
        public List<string> KnownUntrustedNetworks { get; set; } = new();

        [JsonPropertyName("tunnels")]
        public Dictionary<string, TunnelProfile> Tunnels { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("split_tunnel_allowed_ips")]
        public string SplitTunnelAllowedIPs { get; set; } = "";

        [JsonPropertyName("full_tunnel_allowed_ips")]
        public string FullTunnelAllowedIPs { get; set; } = "0.0.0.0/0, ::/0";

        [JsonPropertyName("handshake_timeout_seconds")]
        public int HandshakeTimeoutSeconds { get; set; } = 180;

        [JsonPropertyName("action_on_failure")]
        public string ActionOnFailure { get; set; } = "prompt";

        [JsonPropertyName("theme")]
        public string Theme { get; set; } = "system";

        [JsonPropertyName("start_minimized")]
        public bool StartMinimized { get; set; } = false;

        [JsonPropertyName("start_with_windows")]
        public bool StartWithWindows { get; set; } = false;

        [JsonPropertyName("initial_setup_completed")]
        public bool InitialSetupCompleted { get; set; } = false;

        [JsonPropertyName("check_for_updates")]
        public bool CheckForUpdates { get; set; } = true;

        [JsonPropertyName("last_update_check_utc")]
        public DateTime? LastUpdateCheckUtc { get; set; }

        [JsonPropertyName("ignored_update_version")]
        public string? IgnoredUpdateVersion { get; set; }

        public TunnelProfile GetProfile(string? tunnelName)
        {
            string key = string.IsNullOrWhiteSpace(tunnelName)
                ? (string.IsNullOrWhiteSpace(WireGuardInterface) ? "wg0" : WireGuardInterface)
                : tunnelName.Trim();

            if (Tunnels.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // Create new profile with smart defaults (or migrate from legacy top-level values)
            var profile = new TunnelProfile
            {
                WireGuardConfigPath = string.Equals(key, WireGuardInterface, StringComparison.OrdinalIgnoreCase) ? WireGuardConfigPath : null,
                SplitTunnelAllowedIPs = string.Equals(key, WireGuardInterface, StringComparison.OrdinalIgnoreCase) ? (SplitTunnelAllowedIPs ?? "") : "",
                FullTunnelAllowedIPs = string.Equals(key, WireGuardInterface, StringComparison.OrdinalIgnoreCase) ? (FullTunnelAllowedIPs ?? "0.0.0.0/0, ::/0") : "0.0.0.0/0, ::/0",
                HandshakeTimeoutSeconds = string.Equals(key, WireGuardInterface, StringComparison.OrdinalIgnoreCase) && HandshakeTimeoutSeconds > 0 ? HandshakeTimeoutSeconds : 180
            };

            Tunnels[key] = profile;
            return profile;
        }

        public void SetProfile(string tunnelName, TunnelProfile profile)
        {
            if (string.IsNullOrWhiteSpace(tunnelName)) return;
            string key = tunnelName.Trim();
            Tunnels[key] = profile;

            if (string.Equals(key, WireGuardInterface, StringComparison.OrdinalIgnoreCase))
            {
                WireGuardConfigPath = profile.WireGuardConfigPath;
                SplitTunnelAllowedIPs = profile.SplitTunnelAllowedIPs;
                FullTunnelAllowedIPs = profile.FullTunnelAllowedIPs;
                HandshakeTimeoutSeconds = profile.HandshakeTimeoutSeconds;
            }
        }
    }
}

