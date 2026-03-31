using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;

namespace TunnelFlow.Tests.Capture;

public class WfpTcpRedirectProviderEventIngestionTests
{
    [Fact]
    public async Task SyntheticNativeEvent_IsIngestedIntoMetadataStore()
    {
        var store = new InMemoryOriginalDestinationStore();
        var nativeSession = new WfpNativeSession(
            new WfpNativeInterop(),
            NullLogger<WfpNativeSession>.Instance);
        var provider = new WfpTcpRedirectProvider(
            store,
            nativeSession,
            NullLogger<WfpTcpRedirectProvider>.Instance);

        await provider.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true,
            RecordTtl = TimeSpan.FromMinutes(3)
        });

        var correlationId = Guid.NewGuid();
        var redirectEvent = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.5"), 54321),
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 8443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ProcessId = 4321,
            ProcessPath = @"C:\Apps\TestApp.exe",
            AppId = "test-app-id",
            CorrelationId = correlationId,
            ObservedAtUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        nativeSession.PublishSyntheticEvent(redirectEvent);

        Assert.True(provider.TryGetOriginalDestination(redirectEvent.LookupKey, out var stored));
        Assert.Equal(redirectEvent.OriginalDestination, stored.OriginalDestination);
        Assert.Equal(redirectEvent.RelayEndpoint, stored.RelayEndpoint);
        Assert.Equal(redirectEvent.ProcessId, stored.ProcessId);
        Assert.Equal(redirectEvent.ProcessPath, stored.ProcessPath);
        Assert.Equal(correlationId, stored.CorrelationId);
        Assert.Equal(redirectEvent.ObservedAtUtc.AddMinutes(3), stored.ExpiresAtUtc);

        var stats = provider.GetStats();
        Assert.True(stats.ProviderStarted);
        Assert.Equal(nameof(WfpTcpRedirectProvider), stats.ActiveProviderName);
        Assert.Equal(1, stats.RedirectRegistrationCount);
        Assert.Equal(1, stats.LookupHitCount);
        Assert.Equal(1, stats.ActiveRecordCount);

        await provider.StopAsync();
    }
}
