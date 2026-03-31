using System.Net;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;

namespace TunnelFlow.Tests.Capture;

public class WfpNativeContractTests
{
    [Fact]
    public void RedirectEventPayload_RoundTripsThroughNativeContract()
    {
        var observedAt = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var correlationId = Guid.NewGuid();
        var redirectEvent = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.50"), 54321),
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ProcessId = 4242,
            ProcessPath = @"C:\Apps\Floorp\floorp.exe",
            AppId = "floorp-app-id",
            CorrelationId = correlationId,
            ObservedAtUtc = observedAt
        };

        byte[] payload = WfpNativeContract.BuildRedirectEventPayload(redirectEvent);
        bool parsed = WfpNativeContract.TryParseRedirectEvent(payload, out var actual);

        Assert.True(parsed);
        Assert.Equal(redirectEvent.LookupKey, actual.LookupKey);
        Assert.Equal(redirectEvent.OriginalDestination, actual.OriginalDestination);
        Assert.Equal(redirectEvent.RelayEndpoint, actual.RelayEndpoint);
        Assert.Equal(redirectEvent.ProcessId, actual.ProcessId);
        Assert.Equal(redirectEvent.ProcessPath, actual.ProcessPath);
        Assert.Equal(redirectEvent.AppId, actual.AppId);
        Assert.Equal(redirectEvent.CorrelationId, actual.CorrelationId);
        Assert.Equal(redirectEvent.ObservedAtUtc, actual.ObservedAtUtc);
    }

    [Fact]
    public void ConfigureRequest_EncodesRelayEndpointAndProcessPath()
    {
        var config = new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true,
            EnableDetailedLogging = true,
            TestProcessPath = @"C:\Apps\Floorp\floorp.exe",
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070)
        };

        byte[] payload = WfpNativeContract.BuildConfigureRequest(config);

        Assert.Equal(System.Runtime.InteropServices.Marshal.SizeOf<WfpNativeConfigureRequestV1>(), payload.Length);

        var request = WfpNativeContract.ReadConfigureRequestInfo(payload);

        Assert.NotNull(request);
        Assert.Equal(WfpNativeContract.ContractVersion, request.Version);
        Assert.Equal((uint)System.Runtime.InteropServices.Marshal.SizeOf<WfpNativeConfigureRequestV1>(), request.Size);
        Assert.Equal(WfpNativeContract.ConfigureFlagEnableDetailedLogging, request.Flags);
        Assert.Equal((ushort)2070, request.RelayPort);
        Assert.Equal(@"C:\Apps\Floorp\floorp.exe", request.TestProcessPath);
    }
}
