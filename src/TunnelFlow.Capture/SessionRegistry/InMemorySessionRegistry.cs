using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.SessionRegistry;

public sealed class InMemorySessionRegistry : ISessionRegistry
{
    private const int MaxSessions = 10_000;
    private const int EvictCount = 100;

    private readonly ConcurrentDictionary<ulong, SessionEntry> _sessions = new();
    private readonly ILogger<InMemorySessionRegistry> _logger;

    public InMemorySessionRegistry(ILogger<InMemorySessionRegistry> logger) => _logger = logger;

    public void Add(SessionEntry entry)
    {
        if (_sessions.Count >= MaxSessions)
        {
            EvictLru();
        }

        var now = DateTime.UtcNow;
        var prepared = entry with
        {
            CreatedAt = now,
            LastActivityAt = now,
            State = SessionState.Active
        };
        _sessions[prepared.FlowId] = prepared;
    }

    public bool TryGet(ulong flowId, out SessionEntry? entry)
    {
        bool found = _sessions.TryGetValue(flowId, out var stored);
        entry = stored;
        return found;
    }

    public void Remove(ulong flowId)
    {
        if (_sessions.TryRemove(flowId, out var entry))
        {
            entry.State = SessionState.Closed;
        }
    }

    public void UpdateActivity(ulong flowId)
    {
        if (_sessions.TryGetValue(flowId, out var entry))
        {
            entry.LastActivityAt = DateTime.UtcNow;
        }
    }

    public void PurgeExpiredUdp(TimeSpan idleTimeout)
    {
        var now = DateTime.UtcNow;
        int purged = 0;

        foreach (var kvp in _sessions)
        {
            if (kvp.Value.Protocol == Protocol.Udp
                && kvp.Value.State == SessionState.Active
                && (now - kvp.Value.LastActivityAt) > idleTimeout)
            {
                if (_sessions.TryRemove(kvp.Key, out var removed))
                {
                    removed.State = SessionState.Closed;
                    purged++;
                }
            }
        }

        if (purged > 0)
        {
            _logger.LogDebug("Purged {Count} expired UDP sessions", purged);
        }
    }

    public IReadOnlyList<SessionEntry> GetAll() => _sessions.Values.ToList();

    private void EvictLru()
    {
        var toEvict = _sessions
            .OrderBy(kvp => kvp.Value.LastActivityAt)
            .Take(EvictCount)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in toEvict)
        {
            _sessions.TryRemove(id, out _);
        }

        _logger.LogWarning(
            "Session registry LRU eviction: removed {Count} entries (capacity {Max})",
            toEvict.Count, MaxSessions);
    }
}
