using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record OwnerHeartbeatCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public record OwnerHeartbeatPayload
{
    [JsonPropertyName("ownerSessionId")]
    public required string OwnerSessionId { get; init; }
}
