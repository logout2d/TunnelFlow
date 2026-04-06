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

    [JsonPropertyName("activeProfileName")]
    public string? ActiveProfileName { get; init; }

    [JsonPropertyName("activeOwnerSessionId")]
    public string? ActiveOwnerSessionId { get; init; }

    [JsonPropertyName("captureRunning")]
    public bool CaptureRunning { get; init; }

    [JsonPropertyName("lifecycleState")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TunnelLifecycleState LifecycleState { get; init; }

    [JsonPropertyName("singBoxStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SingBoxStatus SingBoxStatus { get; init; }

    [JsonPropertyName("selectedMode")]
    public TunnelStatusMode SelectedMode { get; init; }

    [JsonPropertyName("singBoxRunning")]
    public bool SingBoxRunning { get; init; }

    [JsonPropertyName("tunnelInterfaceUp")]
    public bool TunnelInterfaceUp { get; init; }

    [JsonPropertyName("proxyRuleCount")]
    public int ProxyRuleCount { get; init; }

    [JsonPropertyName("directRuleCount")]
    public int DirectRuleCount { get; init; }

    [JsonPropertyName("blockRuleCount")]
    public int BlockRuleCount { get; init; }

    [JsonPropertyName("runtimeWarning")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RuntimeWarningEvidence RuntimeWarning { get; init; }
}
