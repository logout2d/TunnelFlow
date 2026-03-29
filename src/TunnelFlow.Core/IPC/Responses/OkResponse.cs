using System.Text.Json;
using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Responses;

public record OkResponse
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }
}
