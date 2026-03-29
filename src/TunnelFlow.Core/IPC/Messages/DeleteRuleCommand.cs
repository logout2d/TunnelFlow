using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record DeleteRuleCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required DeleteRulePayload Payload { get; init; }
}

public record DeleteRulePayload
{
    [JsonPropertyName("ruleId")]
    public required Guid RuleId { get; init; }
}
