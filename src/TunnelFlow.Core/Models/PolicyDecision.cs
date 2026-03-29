using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

public record PolicyDecision
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PolicyAction Action { get; init; }

    public string? Reason { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PolicyAction
{
    Proxy,
    Direct,
    Block
}
