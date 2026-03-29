using System.Net;
using System.Text.Json.Serialization;

namespace TunnelFlow.Core.Models;

public record SessionEntry
{
    public ulong FlowId { get; init; }

    public int ProcessId { get; init; }

    public string ProcessPath { get; init; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Protocol Protocol { get; init; }

    public required IPEndPoint OriginalSource { get; init; }

    public required IPEndPoint OriginalDestination { get; init; }

    public required PolicyDecision Decision { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime LastActivityAt { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionState State { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Protocol
{
    Tcp,
    Udp
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionState
{
    Active,
    Closing,
    Closed
}
