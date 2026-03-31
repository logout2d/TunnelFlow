using System.Net;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Tests.Capture;

public class WfpRedirectEventTests
{
    [Fact]
    public void ToConnectionRedirectRecord_PreservesEndpointsIdentityAndTtl()
    {
        var observedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var correlationId = Guid.NewGuid();
        var redirectEvent = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.5"), 54321),
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ProcessId = 1234,
            ProcessPath = @"C:\Apps\Floorp\floorp.exe",
            AppId = "floorp-app-id",
            Protocol = Protocol.Tcp,
            CorrelationId = correlationId,
            ObservedAtUtc = observedAt
        };

        var record = redirectEvent.ToConnectionRedirectRecord(TimeSpan.FromMinutes(2));

        Assert.Equal(redirectEvent.LookupKey, record.LookupKey);
        Assert.Equal(redirectEvent.OriginalDestination, record.OriginalDestination);
        Assert.Equal(redirectEvent.RelayEndpoint, record.RelayEndpoint);
        Assert.Equal(redirectEvent.ProcessId, record.ProcessId);
        Assert.Equal(redirectEvent.ProcessPath, record.ProcessPath);
        Assert.Equal(redirectEvent.Protocol, record.Protocol);
        Assert.Equal(correlationId, record.CorrelationId);
        Assert.Equal(observedAt, record.CreatedAtUtc);
        Assert.Equal(observedAt.AddMinutes(2), record.ExpiresAtUtc);
    }
}
