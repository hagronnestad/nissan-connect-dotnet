using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class AuthenticateAuthIdResponse
    {
        [JsonPropertyName("authId")]
        public string? AuthId { get; set; }

        [JsonPropertyName("template")]
        public string? Template { get; set; }

        [JsonPropertyName("stage")]
        public string? Stage { get; set; }

        [JsonPropertyName("header")]
        public string? Header { get; set; }

        [JsonPropertyName("callbacks")]
        public List<Callback>? Callbacks { get; set; }
    }
}
