using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class UserIdResult
    {
        [JsonPropertyName("userId")]
        public string? UserId { get; set; }
    }
}
