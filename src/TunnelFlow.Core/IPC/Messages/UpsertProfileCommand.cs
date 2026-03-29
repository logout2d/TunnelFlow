using System.Text.Json.Serialization;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Core.IPC.Messages;

public record UpsertProfileCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required VlessProfile Payload { get; init; }
}
