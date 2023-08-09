using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class AuthenticateTokenIdResponse
    {
        [JsonPropertyName("tokenId")]
        public string? TokenId { get; set; }

        [JsonPropertyName("successUrl")]
        public string? SuccessUrl { get; set; }

        [JsonPropertyName("realm")]
        public string? Realm { get; set; }
    }
}
