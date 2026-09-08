using System;

namespace WireFox.Models
{
    public class NetworkState
    {
        public string Ssid { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public bool IsWireless { get; set; }
        public bool IsTrusted { get; set; }
        public string? Bssid { get; set; }
        public string AdapterName { get; set; } = string.Empty;
        public string ConnectionType { get; set; } = "Disconnected";
        public string? LocalIp { get; set; }
        public string? SubnetMask { get; set; }
        public string? GatewayIp { get; set; }
        public string? GatewayMac { get; set; }
        public string? DnsServerName { get; set; }
        public string? DnsSuffix { get; set; }
        public System.Collections.Generic.List<string> DetectedNames { get; set; } = new();
        public bool IsGatewayMacMismatch { get; set; }
        public string? RegisteredGatewayMac { get; set; }
        public DateTime LastCheckedUtc { get; set; } = DateTime.UtcNow;

        public override string ToString()
        {
            if (!IsConnected) return "Disconnected";
            if (IsWireless && !string.IsNullOrWhiteSpace(Ssid)) return Ssid;
            if (!string.IsNullOrWhiteSpace(GatewayIp)) return $"{ConnectionType} ({GatewayIp})";
            return ConnectionType;
        }
    }
}
