using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class Callback
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("input")]
        public List<Input>? Input { get; set; }

        [JsonPropertyName("output")]
        public List<Output>? Output { get; set; }
    }

    public class Input
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    public class Output : Input { }
}
