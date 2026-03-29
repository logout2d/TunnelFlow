using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Capture.SessionRegistry;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Tests.Capture;

public class SessionRegistryTests
{
    private readonly InMemorySessionRegistry _registry =
        new(NullLogger<InMemorySessionRegistry>.Instance);

    private static SessionEntry CreateEntry(
        ulong flowId,
        Protocol protocol = Protocol.Tcp) => new()
    {
        FlowId = flowId,
        ProcessId = 1,
        ProcessPath = "test.exe",
        Protocol = protocol,
        OriginalSource = new IPEndPoint(IPAddress.Loopback, 1234),
        OriginalDestination = new IPEndPoint(IPAddress.Loopback, 80),
        Decision = new PolicyDecision { Action = PolicyAction.Proxy }
    };

    [Fact]
    public void Add_And_TryGet_Roundtrip()
    {
        _registry.Add(CreateEntry(1));

        bool found = _registry.TryGet(1, out var entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal(1ul, entry.FlowId);
        Assert.Equal(SessionState.Active, entry.State);
        Assert.True(entry.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void PurgeExpiredUdp_RemovesOnlyExpiredUdpSessions()
    {
        _registry.Add(CreateEntry(1, Protocol.Udp));
        _registry.Add(CreateEntry(2, Protocol.Udp));

        // Make entry 1 old
        if (_registry.TryGet(1, out var expired))
            expired!.LastActivityAt = DateTime.UtcNow.AddSeconds(-60);

        _registry.PurgeExpiredUdp(TimeSpan.FromSeconds(30));

        Assert.False(_registry.TryGet(1, out _));
        Assert.True(_registry.TryGet(2, out _));
    }

    [Fact]
    public void PurgeExpiredUdp_DoesNotRemoveTcpSessions()
    {
        _registry.Add(CreateEntry(1, Protocol.Tcp));

        if (_registry.TryGet(1, out var entry))
            entry!.LastActivityAt = DateTime.UtcNow.AddSeconds(-60);

        _registry.PurgeExpiredUdp(TimeSpan.FromSeconds(30));

        Assert.True(_registry.TryGet(1, out _));
    }

    [Fact]
    public void LruEviction_WhenCountReaches10000()
    {
        for (ulong i = 0; i < 10_000; i++)
        {
            _registry.Add(CreateEntry(i));
        }

        // Make entries 0–99 old so they are eviction candidates
        for (ulong i = 0; i < 100; i++)
        {
            if (_registry.TryGet(i, out var e))
                e!.LastActivityAt = DateTime.UtcNow.AddHours(-1);
        }

        // This add triggers eviction of 100 LRU entries before inserting
        _registry.Add(CreateEntry(10_000));

        var all = _registry.GetAll();
        Assert.Equal(9_901, all.Count);

        // The old entries should have been evicted
        for (ulong i = 0; i < 100; i++)
        {
            Assert.False(_registry.TryGet(i, out _), $"Entry {i} should have been evicted");
        }

        // Recent entries and the new one should remain
        Assert.True(_registry.TryGet(10_000, out _));
        Assert.True(_registry.TryGet(9_999, out _));
    }

    [Fact]
    public void Remove_SetsStateToClosed()
    {
        _registry.Add(CreateEntry(42));
        _registry.TryGet(42, out var before);
        Assert.Equal(SessionState.Active, before!.State);

        _registry.Remove(42);

        Assert.Equal(SessionState.Closed, before.State);
        Assert.False(_registry.TryGet(42, out _));
    }

    [Fact]
    public void UpdateActivity_UpdatesLastActivityAt()
    {
        _registry.Add(CreateEntry(7));
        _registry.TryGet(7, out var entry);
        var firstActivity = entry!.LastActivityAt;

        Thread.Sleep(15);
        _registry.UpdateActivity(7);

        _registry.TryGet(7, out var updated);
        Assert.True(updated!.LastActivityAt > firstActivity);
    }
}
