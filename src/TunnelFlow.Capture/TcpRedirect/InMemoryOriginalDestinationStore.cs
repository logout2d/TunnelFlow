using System.Collections.Concurrent;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class InMemoryOriginalDestinationStore : IOriginalDestinationStore
{
    private readonly ConcurrentDictionary<ConnectionLookupKey, ConnectionRedirectRecord> _records = new();

    public void Add(ConnectionRedirectRecord record) =>
        _records[record.LookupKey] = record;

    public bool TryGet(ConnectionLookupKey key, out ConnectionRedirectRecord record) =>
        _records.TryGetValue(key, out record!);

    public void Remove(ConnectionLookupKey key) =>
        _records.TryRemove(key, out _);

    public int PurgeExpired(DateTime utcNow)
    {
        int removed = 0;

        foreach (var pair in _records)
        {
            if (pair.Value.ExpiresAtUtc > utcNow)
                continue;

            if (_records.TryRemove(pair.Key, out _))
                removed++;
        }

        return removed;
    }
}
