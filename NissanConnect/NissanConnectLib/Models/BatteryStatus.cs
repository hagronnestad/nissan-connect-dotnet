using NissanConnectLib.Enums;
using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class BatteryStatus
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("plugStatusDetail")]
        public int PlugStatusDetail { get; set; }

        [JsonPropertyName("timeRequiredToFullSlow")]
        public int TimeRequiredToFullSlow { get; set; }

        [JsonPropertyName("plugStatus")]
        public PlugStatus PlugStatus { get; set; }

        [JsonPropertyName("chargeStatus")]
        public ChargeStatus ChargeStatus { get; set; }

        [JsonPropertyName("batteryCapacity")]
        public int BatteryCapacity { get; set; }

        [JsonPropertyName("timeRequiredToFullFast")]
        public int TimeRequiredToFullFast { get; set; }

        [JsonPropertyName("batteryLevel")]
        public int BatteryLevel { get; set; }

        [JsonPropertyName("timeRequiredToFullNormal")]
        public int TimeRequiredToFullNormal { get; set; }

        [JsonPropertyName("rangeHvacOn")]
        public int RangeHvacOn { get; set; }

        [JsonPropertyName("rangeHvacOff")]
        public int RangeHvacOff { get; set; }

        [JsonPropertyName("batteryBarLevel")]
        public int BatteryBarLevel { get; set; }

        [JsonPropertyName("lastUpdateTime")]
        public DateTimeOffset? LastUpdateTime { get; set; }

        [JsonPropertyName("chargePower")]
        public ChargePower ChargePower { get; set; }


        public TimeSpan? BatteryStatusAge => DateTimeOffset.UtcNow - LastUpdateTime?.UtcDateTime;
    }
}
