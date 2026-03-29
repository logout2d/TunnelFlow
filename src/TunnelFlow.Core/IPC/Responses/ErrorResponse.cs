using System.Text.Json.Serialization;

namespace TunnelFlow.Core.IPC.Responses;

public record ErrorResponse
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("payload")]
    public required ErrorPayload Payload { get; init; }
}

public record ErrorPayload
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
