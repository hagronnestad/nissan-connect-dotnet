namespace NissanConnectLib.Models
{
    public class BatteryStatusResult
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
        public BatteryStatus? Attributes { get; set; }
    }
}
