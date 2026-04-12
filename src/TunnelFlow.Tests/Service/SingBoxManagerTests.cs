using System.Diagnostics;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.SingBox;

namespace TunnelFlow.Tests.Service;

public class SingBoxManagerTests
{
    [Fact]
    public async Task WaitForTunStartupReadinessAsync_ReturnsFalse_WhenProcessExitsDuringWindow()
    {
        var stopwatch = Stopwatch.StartNew();

        var ready = await SingBoxManager.WaitForTunStartupReadinessAsync(
            hasExited: () => stopwatch.ElapsedMilliseconds > 40,
            getStartupFailure: () => null,
            observationWindow: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(10),
            ct: CancellationToken.None);

        Assert.False(ready.Ready);
        Assert.Equal("process-exited-during-startup-window", ready.Reason);
    }

    [Fact]
    public async Task WaitForTunStartupReadinessAsync_ReturnsFalse_WhenStartupFatalLineIsObserved()
    {
        var stopwatch = Stopwatch.StartNew();

        var ready = await SingBoxManager.WaitForTunStartupReadinessAsync(
            hasExited: () => false,
            getStartupFailure: () => stopwatch.ElapsedMilliseconds > 40
                ? new SingBoxReadinessResult(false, "startup-fatal-tun-log-line")
                : null,
            observationWindow: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(10),
            ct: CancellationToken.None);

        Assert.False(ready.Ready);
        Assert.Equal("startup-fatal-tun-log-line", ready.Reason);
    }

    [Fact]
    public async Task WaitForTunStartupReadinessAsync_ReturnsTrue_WhenWindowPassesWithoutExitOrFatalLine()
    {
        var ready = await SingBoxManager.WaitForTunStartupReadinessAsync(
            hasExited: () => false,
            getStartupFailure: () => null,
            observationWindow: TimeSpan.FromMilliseconds(50),
            pollInterval: TimeSpan.FromMilliseconds(10),
            ct: CancellationToken.None);

        Assert.True(ready.Ready);
        Assert.Equal("startup-window-passed-without-fatal-tun-signals", ready.Reason);
    }

    [Theory]
    [InlineData("FATAL start service: start inbound/tun[tun-in]: configurate tun interface: Cannot create a file when that file already exist", "FATAL")]
    [InlineData("WARN inbound/tun[tun-in]: open interface take too much time to finish!", "open interface take too much time to finish")]
    [InlineData("start inbound/tun[tun-in]: configure tun interface", "configure tun interface")]
    [InlineData("Cannot create a file when that file already exist", "Cannot create a file when that file already exist")]
    public void TryMatchTunStartupFatalLine_MatchesKnownTunStartupFailures(string line, string expectedPattern)
    {
        var matched = SingBoxManager.TryMatchTunStartupFatalLine(line, out var pattern);

        Assert.True(matched);
        Assert.Equal(expectedPattern, pattern);
    }

    [Fact]
    public async Task EnsureCleanLogOutputFileAsync_TruncatesExistingLogFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var logPath = Path.Combine(tempDir, "singbox.log");
            await File.WriteAllTextAsync(logPath, "old-log-content");

            await SingBoxManager.EnsureCleanLogOutputFileAsync(logPath, CancellationToken.None);

            var content = await File.ReadAllTextAsync(logPath);
            Assert.Equal(string.Empty, content);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
