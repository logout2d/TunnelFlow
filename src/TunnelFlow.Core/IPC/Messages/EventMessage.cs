using System.Text.Json;
using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

/// <summary>
/// Push event from service to UI (no request <c>id</c>); <c>type</c> identifies the event shape in <c>payload</c>.
/// </summary>
public record EventMessage
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}
