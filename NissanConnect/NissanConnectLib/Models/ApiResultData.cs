using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class ApiResultData<T>
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("attributes")]
        public T? Attributes { get; set; }
    }
}
