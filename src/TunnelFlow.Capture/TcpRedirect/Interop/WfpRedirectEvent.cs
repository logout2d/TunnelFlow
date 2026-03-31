using System.Net;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.TcpRedirect.Interop;

public sealed record WfpRedirectEvent
{
    public required ConnectionLookupKey LookupKey { get; init; }

    public required IPEndPoint OriginalDestination { get; init; }

    public required IPEndPoint RelayEndpoint { get; init; }

    public int? ProcessId { get; init; }

    public string? ProcessPath { get; init; }

    public string? AppId { get; init; }

    public Protocol Protocol { get; init; } = Protocol.Tcp;

    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    public DateTime ObservedAtUtc { get; init; } = DateTime.UtcNow;

    public ConnectionRedirectRecord ToConnectionRedirectRecord(TimeSpan ttl) => new()
    {
        LookupKey = LookupKey,
        OriginalDestination = OriginalDestination,
        RelayEndpoint = RelayEndpoint,
        ProcessId = ProcessId,
        ProcessPath = ProcessPath,
        Protocol = Protocol,
        CorrelationId = CorrelationId,
        CreatedAtUtc = ObservedAtUtc,
        ExpiresAtUtc = ObservedAtUtc.Add(ttl)
    };
}
