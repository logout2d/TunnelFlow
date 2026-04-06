using System.Text.Json.Serialization;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Core.IPC.Responses;

public record StatusPayload
{
    [JsonPropertyName("captureRunning")]
    public bool CaptureRunning { get; init; }

    [JsonPropertyName("lifecycleState")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TunnelLifecycleState LifecycleState { get; init; }

    [JsonPropertyName("singboxStatus")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SingBoxStatus SingBoxStatus { get; init; }

    [JsonPropertyName("selectedMode")]
    public TunnelStatusMode SelectedMode { get; init; }

    [JsonPropertyName("singBoxRunning")]
    public bool SingBoxRunning { get; init; }

    [JsonPropertyName("tunnelInterfaceUp")]
    public bool TunnelInterfaceUp { get; init; }

    [JsonPropertyName("activeProfileId")]
    public Guid? ActiveProfileId { get; init; }

    [JsonPropertyName("activeProfileName")]
    public string? ActiveProfileName { get; init; }

    [JsonPropertyName("activeOwnerSessionId")]
    public string? ActiveOwnerSessionId { get; init; }

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
