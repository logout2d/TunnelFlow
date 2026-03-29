using TunnelFlow.Core.Models;

namespace TunnelFlow.Core;

/// <summary>
/// In-memory registry of active capture sessions (TCP flows and UDP associations) keyed by WinpkFilter flow id.
/// </summary>
public interface ISessionRegistry
{
    void Add(SessionEntry entry);

    bool TryGet(ulong flowId, out SessionEntry? entry);

    void Remove(ulong flowId);

    void UpdateActivity(ulong flowId);

    /// <summary>Clean up UDP sessions that exceeded idle timeout.</summary>
    void PurgeExpiredUdp(TimeSpan idleTimeout);

    IReadOnlyList<SessionEntry> GetAll();
}
