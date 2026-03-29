using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Messages;

public record ActivateProfileCommand
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required ActivateProfilePayload Payload { get; init; }
}

public record ActivateProfilePayload
{
    [JsonPropertyName("profileId")]
    public required Guid ProfileId { get; init; }
}
