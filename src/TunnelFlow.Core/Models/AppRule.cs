using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

public record AppRule
{
    public Guid Id { get; init; }

    public string ExePath { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

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
