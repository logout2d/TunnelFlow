using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record GetStateCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}
