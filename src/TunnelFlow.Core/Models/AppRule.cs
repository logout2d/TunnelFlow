using System.Text.Json;
using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

public record AppRule
{
    public Guid Id { get; init; }

    public string ExePath { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("matchType")]
    public AppRuleMatchType MatchType { get; init; } = AppRuleMatchType.FullPath;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RuleMode Mode { get; init; }

    public bool IsEnabled { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleMode
{
    Proxy,
    Direct,
    Block
}

[JsonConverter(typeof(AppRuleMatchTypeJsonConverter))]
public enum AppRuleMatchType
{
    FullPath,
    ExeName
}

public sealed class AppRuleMatchTypeJsonConverter : JsonConverter<AppRuleMatchType>
{
    public override AppRuleMatchType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (string.Equals(value, "fullPath", StringComparison.OrdinalIgnoreCase))
            {
                return AppRuleMatchType.FullPath;
            }

            if (string.Equals(value, "exeName", StringComparison.OrdinalIgnoreCase))
            {
                return AppRuleMatchType.ExeName;
            }

            throw new JsonException($"Unsupported App Rule match type '{value}'.");
        }

        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var number) &&
            Enum.IsDefined(typeof(AppRuleMatchType), number))
        {
            return (AppRuleMatchType)number;
        }

        throw new JsonException("Unsupported App Rule match type token.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        AppRuleMatchType value,
        JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            AppRuleMatchType.FullPath => "fullPath",
            AppRuleMatchType.ExeName => "exeName",
            _ => throw new JsonException($"Unsupported App Rule match type '{value}'.")
        });
    }
}
