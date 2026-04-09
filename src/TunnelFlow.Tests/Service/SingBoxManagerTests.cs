using System.Diagnostics;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.SingBox;

namespace TunnelFlow.Tests.Service;

public class SingBoxManagerTests
{
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
