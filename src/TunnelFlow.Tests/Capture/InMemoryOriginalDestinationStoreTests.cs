using System.Net;
using TunnelFlow.Capture.TcpRedirect;

namespace TunnelFlow.Tests.Capture;

public class InMemoryOriginalDestinationStoreTests
{
    [Fact]
    public void AddTryGetRemove_RoundTripsRecord()
    {
        var store = new InMemoryOriginalDestinationStore();
        var key = new ConnectionLookupKey(IPAddress.Parse("192.168.1.5"), 54321);
        var record = new ConnectionRedirectRecord
        {
            LookupKey = key,
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070)
        };

        store.Add(record);

        Assert.True(store.TryGet(key, out var stored));
        Assert.Equal(record, stored);

        store.Remove(key);

        Assert.False(store.TryGet(key, out _));
    }

    [Fact]
    public void PurgeExpired_RemovesOnlyExpiredRecords()
    {
        var store = new InMemoryOriginalDestinationStore();
        var utcNow = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

        var expiredKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.5"), 54321);
        var activeKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.6"), 54322);

        store.Add(new ConnectionRedirectRecord
        {
            LookupKey = expiredKey,
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ExpiresAtUtc = utcNow.AddSeconds(-1)
        });
        store.Add(new ConnectionRedirectRecord
        {
            LookupKey = activeKey,
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.11"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ExpiresAtUtc = utcNow.AddMinutes(1)
        });

        var removed = store.PurgeExpired(utcNow);

        Assert.Equal(1, removed);
        Assert.False(store.TryGet(expiredKey, out _));
        Assert.True(store.TryGet(activeKey, out _));
    }
}
