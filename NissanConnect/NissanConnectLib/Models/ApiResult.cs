using System.Text.Json.Serialization;

namespace NissanConnectLib.Models
{
    public class ApiResult<T>
    {
        [JsonPropertyName("data")]
        public ApiResultData<T>? Data { get; set; }
    }
}
