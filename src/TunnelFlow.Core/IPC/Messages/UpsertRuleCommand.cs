using System.Text.Json.Serialization;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Core.IPC.Messages;

public record UpsertRuleCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required AppRule Payload { get; init; }
}
