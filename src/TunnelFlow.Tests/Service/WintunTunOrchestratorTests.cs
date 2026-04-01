using Microsoft.Extensions.Logging.Abstractions;
using TunnelFlow.Service.Tun;

namespace TunnelFlow.Tests.Service;

public class WintunTunOrchestratorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public WintunTunOrchestratorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void SupportsActivation_False_WhenWintunDllIsMissing()
    {
        var path = Path.Combine(_tempDir, "wintun.dll");
        var orchestrator = new WintunTunOrchestrator(
            NullLogger<WintunTunOrchestrator>.Instance,
            path,
            _ => 1,
            _ => { });

        Assert.False(orchestrator.SupportsActivation);
        Assert.Equal(path, orchestrator.ResolvedWintunPath);
    }

    [Fact]
    public async Task StartAsync_LoadsLibrary_AndStopAsync_FreesIt_WhenPrerequisitesExist()
    {
        var path = Path.Combine(_tempDir, "wintun.dll");
        await File.WriteAllTextAsync(path, "stub");

        int loadCount = 0;
        int freeCount = 0;
        var orchestrator = new WintunTunOrchestrator(
            NullLogger<WintunTunOrchestrator>.Instance,
            path,
            _ =>
            {
                loadCount++;
                return 123;
            },
            _ => freeCount++);

        Assert.True(orchestrator.SupportsActivation);

        await orchestrator.StartAsync(
            new TunOrchestrationConfig { UseTunMode = true, WintunPath = path },
            CancellationToken.None);
        await orchestrator.StopAsync(CancellationToken.None);

        Assert.Equal(1, loadCount);
        Assert.Equal(1, freeCount);
    }
}
