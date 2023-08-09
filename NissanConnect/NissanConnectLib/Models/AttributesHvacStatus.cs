using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class AttributesHvacStatus
    {
        [JsonPropertyName("socThreshold")]
        public double? SocThreshold { get; set; }

        [JsonPropertyName("lastUpdateTime")]
        public DateTimeOffset? LastUpdateTime { get; set; }

        [JsonPropertyName("hvacStatus")]
        public string? HvacStatus { get; set; }
    }
}
