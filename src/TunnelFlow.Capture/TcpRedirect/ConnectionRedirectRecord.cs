using System.Net;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed record ConnectionRedirectRecord
{
    public required ConnectionLookupKey LookupKey { get; init; }

    public required IPEndPoint OriginalDestination { get; init; }

    public required IPEndPoint RelayEndpoint { get; init; }

    public int? ProcessId { get; init; }

    public string? ProcessPath { get; init; }

    public Protocol Protocol { get; init; } = Protocol.Tcp;

    public Guid CorrelationId { get; init; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddMinutes(2);
}
