using System.Text.Json.Serialization;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Core.IPC.Responses;

/// <summary>Full configuration and runtime snapshot returned for a <c>GetState</c> command.</summary>
public record StatePayload
{
    [JsonPropertyName("rules")]
    public required IReadOnlyList<AppRule> Rules { get; init; }

    [JsonPropertyName("profiles")]
    public required IReadOnlyList<VlessProfile> Profiles { get; init; }

    [JsonPropertyName("activeProfileId")]
    public Guid? ActiveProfileId { get; init; }

    [JsonPropertyName("captureRunning")]
    public bool CaptureRunning { get; init; }

    [JsonPropertyName("singBoxStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SingBoxStatus SingBoxStatus { get; init; }
}
