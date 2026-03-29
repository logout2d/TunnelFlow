using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record GetSessionsCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}
