using System.Text.Json.Serialization;

namespace NissanConnectLib.Models;

public class CarsResult
{
    [JsonPropertyName("data")]
    public List<Car>? Data { get; set; }
}
