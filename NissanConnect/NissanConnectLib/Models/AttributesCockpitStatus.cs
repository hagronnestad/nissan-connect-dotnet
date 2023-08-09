using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class AttributesCockpitStatus
    {
        [JsonPropertyName("totalMileage")]
        public double? TotalMileage { get; set; }
    }
}
