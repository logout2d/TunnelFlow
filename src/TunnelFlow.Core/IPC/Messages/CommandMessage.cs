using System.Text.Json;
using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

/// <summary>
/// Base IPC envelope from UI to service: command <c>type</c>, correlation <c>id</c>, and optional <c>payload</c> (line-delimited JSON).
/// </summary>
public record CommandMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}
