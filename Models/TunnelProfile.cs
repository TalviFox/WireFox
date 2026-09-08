using System.Text.Json.Serialization;

namespace WireFox.Models
{
    public class TunnelProfile
    {
        [JsonPropertyName("wireguard_config_path")]
        public string? WireGuardConfigPath { get; set; }

        [JsonPropertyName("split_tunnel_allowed_ips")]
        public string SplitTunnelAllowedIPs { get; set; } = "";

        [JsonPropertyName("full_tunnel_allowed_ips")]
        public string FullTunnelAllowedIPs { get; set; } = "0.0.0.0/0, ::/0";

        [JsonPropertyName("handshake_timeout_seconds")]
        public int HandshakeTimeoutSeconds { get; set; } = 180;
    }
}
