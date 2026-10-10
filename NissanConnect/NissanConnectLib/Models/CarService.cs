using NissanConnectLib.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NissanConnectLib.Models;

public class CarService
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(CarServiceIdConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("activationState")]
    public string? ActivationState { get; set; }

    [JsonIgnore]
    public CarServiceFeature? Feature =>
        int.TryParse(Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) &&
        Enum.IsDefined((CarServiceFeature)id)
            ? (CarServiceFeature)id
            : null;
}

internal sealed class CarServiceIdConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number => ReadNumber(ref reader),
            _ => throw new JsonException("Vehicle service ID must be a string or number.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }
}
