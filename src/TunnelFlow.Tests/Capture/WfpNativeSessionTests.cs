using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TcpRedirect.Interop;
using Xunit.Sdk;

namespace TunnelFlow.Tests.Capture;

public class WfpNativeSessionTests
{
    [Fact]
    public async Task NativeHelperChannel_EmitsOneRedirectEvent()
    {
        string helperPath = NativeChannelTestHelper.GetHelperPathOrSkip();
        var nativeSession = new WfpNativeSession(
            new WfpNativeInterop(helperPath),
            NullLogger<WfpNativeSession>.Instance,
            TimeSpan.FromMilliseconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tcs = new TaskCompletionSource<WfpRedirectEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        nativeSession.RedirectEventReceived += (_, redirectEvent) => tcs.TrySetResult(redirectEvent);

        await nativeSession.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true
        }, cts.Token);

        var expected = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(IPAddress.Parse("192.168.1.5"), 54321),
            OriginalDestination = new IPEndPoint(IPAddress.Parse("203.0.113.10"), 443),
            RelayEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.10"), 2070),
            ProcessId = 1234,
            ProcessPath = @"C:\Apps\TestApp.exe",
            AppId = "test-app-id",
            CorrelationId = Guid.NewGuid(),
            ObservedAtUtc = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        try
        {
            await nativeSession.PublishSyntheticEventAsync(expected, cts.Token);
            var actual = await tcs.Task.WaitAsync(cts.Token);

            Assert.Equal(expected.LookupKey, actual.LookupKey);
            Assert.Equal(expected.OriginalDestination, actual.OriginalDestination);
            Assert.Equal(expected.RelayEndpoint, actual.RelayEndpoint);
            Assert.Equal(expected.ProcessId, actual.ProcessId);
            Assert.Equal(expected.ProcessPath, actual.ProcessPath);
            Assert.Equal(expected.AppId, actual.AppId);
            Assert.Equal(expected.CorrelationId, actual.CorrelationId);
            Assert.Equal(expected.ObservedAtUtc, actual.ObservedAtUtc);
        }
        finally
        {
            await nativeSession.StopAsync(cts.Token);
        }
    }
}
