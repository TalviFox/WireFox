using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WireFox.Models
{
    public class TrustedGateway
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("ip")]
        public string IpAddress { get; set; } = string.Empty;

        [JsonPropertyName("mac")]
        public string MacAddress { get; set; } = string.Empty;

        [JsonPropertyName("stacked_names")]
        public List<string> StackedNames { get; set; } = new();
    }
}
