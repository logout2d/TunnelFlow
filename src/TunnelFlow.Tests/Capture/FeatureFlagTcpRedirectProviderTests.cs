using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Capture.TcpRedirect;

namespace TunnelFlow.Tests.Capture;

public class FeatureFlagTcpRedirectProviderTests
{
    [Fact]
    public async Task StartAsync_UseWfpFalse_SelectsNoOpProvider()
    {
        var provider = CreateProvider();

        await provider.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = false
        });

        var stats = provider.GetStats();

        Assert.False(stats.UseWfpTcpRedirect);
        Assert.True(stats.ProviderStarted);
        Assert.Equal(nameof(NoOpTcpRedirectProvider), stats.ActiveProviderName);
    }

    [Fact]
    public async Task StartAsync_UseWfpTrue_SelectsWfpStubProvider()
    {
        var provider = CreateProvider();

        await provider.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true
        });

        var stats = provider.GetStats();

        Assert.True(stats.UseWfpTcpRedirect);
        Assert.True(stats.ProviderStarted);
        Assert.Equal(nameof(WfpTcpRedirectProvider), stats.ActiveProviderName);
    }

    [Fact]
    public async Task StopAsync_PreservesSelectionAndMarksProviderStopped()
    {
        var provider = CreateProvider();
        await provider.StartAsync(new WfpRedirectConfig
        {
            UseWfpTcpRedirect = true
        });

        await provider.StopAsync();

        var stats = provider.GetStats();

        Assert.True(stats.UseWfpTcpRedirect);
        Assert.False(stats.ProviderStarted);
        Assert.Equal(nameof(WfpTcpRedirectProvider), stats.ActiveProviderName);
    }

    private static FeatureFlagTcpRedirectProvider CreateProvider()
    {
        var store = new InMemoryOriginalDestinationStore();
        var noOpProvider = new NoOpTcpRedirectProvider(store, NullLogger<NoOpTcpRedirectProvider>.Instance);
        var wfpProvider = new WfpTcpRedirectProvider(store, NullLogger<WfpTcpRedirectProvider>.Instance);

        return new FeatureFlagTcpRedirectProvider(
            noOpProvider,
            wfpProvider,
            NullLogger<FeatureFlagTcpRedirectProvider>.Instance);
    }
}
