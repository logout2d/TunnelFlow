using TunnelFlow.Core.Models;

namespace TunnelFlow.Core;

/// <summary>
/// Abstracts WinpkFilter-based packet capture: start/stop interception, session snapshots, and diagnostic events.
/// </summary>
public interface ICaptureEngine : IDisposable
{
    /// <summary>Start packet interception. Must be called from elevated context.</summary>
    Task StartAsync(CaptureConfig config, CancellationToken ct);

    /// <summary>Gracefully stop — drain active sessions, release WinpkFilter handle.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>Live session snapshot for diagnostics.</summary>
    IReadOnlyList<SessionEntry> GetActiveSessions();

    /// <summary>Raised when a new session is established.</summary>
    event EventHandler<SessionEntry>? SessionCreated;

    /// <summary>Raised when a session ends.</summary>
    event EventHandler<SessionEntry>? SessionClosed;

    /// <summary>Raised on capture errors (driver disconnects, etc.).</summary>
    event EventHandler<CaptureError>? ErrorOccurred;
}
