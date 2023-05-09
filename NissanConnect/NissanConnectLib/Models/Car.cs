using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class Car
    {
        [JsonPropertyName("vin")]
        public string? Vin { get; set; }

        [JsonPropertyName("color")]
        public string? Color { get; set; }

        [JsonPropertyName("modelName")]
        public string? ModelName { get; set; }

        [JsonPropertyName("nickname")]
        public string? NickName { get; set; }

        [JsonPropertyName("energy")]
        public string? Energy { get; set; }

        [JsonPropertyName("pictureURL")]
        public string? PictureUrl { get; set; }

        [JsonPropertyName("registrationNumber")]
        public string? RegistrationNumber { get; set; }

        [JsonPropertyName("firstRegistrationDate")]
        public DateTimeOffset? FirstRegistrationDate { get; set; }

        [JsonPropertyName("batteryCode")]
        public string? BatteryCode { get; set; }

        [JsonPropertyName("engineType")]
        public string? EngineType { get; set; }

        [JsonPropertyName("syncStatus")]
        public string? SyncStatus { get; set; }

        [JsonPropertyName("carGateway")]
        public string? CarGateway { get; set; }

        [JsonPropertyName("phase")]
        public int Phase { get; set; }

        [JsonPropertyName("privacyMode")]
        public string? PrivacyMode { get; set; }

        [JsonPropertyName("iceEvFlag")]
        public string? IceEvFlag { get; set; }

        [JsonPropertyName("canGeneration")]
        public string? CanGeneration { get; set; }

        [JsonPropertyName("stolenVehicleFlag")]
        public bool StolenVehicleFlag { get; set; }

        [JsonPropertyName("modelCode")]
        public string? ModelCode { get; set; }

        [JsonPropertyName("vinHash")]
        public string? VinHash { get; set; }

        [JsonPropertyName("vinCrypt")]
        public string? VinCrypt { get; set; }

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("vidInt")]
        public int VidInt { get; set; }

        [JsonPropertyName("vehicleOwnedSince")]
        public DateTimeOffset? VehicleOwnedSince { get; set; }

        [JsonPropertyName("modelYear")]
        public string? ModelYear { get; set; }

        [JsonPropertyName("vehicleNickName")]
        public string? VehicleNickName { get; set; }
    }
}
