using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record DeleteProfileCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required DeleteProfilePayload Payload { get; init; }
}

public record DeleteProfilePayload
{
    [JsonPropertyName("profileId")]
    public required Guid ProfileId { get; init; }
}
