using System.Diagnostics;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.SingBox;

namespace TunnelFlow.Tests.Service;

public class SingBoxManagerTests
{
    [Fact]
    public void SelectReadinessStrategy_UsesSocksPort_ForLegacyMode()
    {
        var config = new SingBoxConfig { UseTunMode = false };

        var strategy = SingBoxManager.SelectReadinessStrategy(config);

        Assert.Equal(SingBoxReadinessStrategy.SocksPort, strategy);
    }

    [Fact]
    public void SelectReadinessStrategy_UsesProcessObservation_ForTunMode()
    {
        var config = new SingBoxConfig { UseTunMode = true };

        var strategy = SingBoxManager.SelectReadinessStrategy(config);

        Assert.Equal(SingBoxReadinessStrategy.ProcessObservation, strategy);
    }

    [Fact]
    public async Task WaitForProcessObservationAsync_ReturnsFalse_WhenProcessExitsDuringWindow()
    {
        var stopwatch = Stopwatch.StartNew();

        var ready = await SingBoxManager.WaitForProcessObservationAsync(
            hasExited: () => stopwatch.ElapsedMilliseconds > 40,
            observationWindow: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(10),
            ct: CancellationToken.None);

        Assert.False(ready.Ready);
        Assert.Equal("process-exited-during-startup-window", ready.Reason);
    }

    [Fact]
    public async Task WaitForProcessObservationAsync_ReturnsTrue_WhenProcessStaysAliveForWindow()
    {
        var ready = await SingBoxManager.WaitForProcessObservationAsync(
            hasExited: () => false,
            observationWindow: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10),
            ct: CancellationToken.None);

        Assert.True(ready.Ready);
        Assert.Equal("process-stable-during-startup-window", ready.Reason);
    }
}
