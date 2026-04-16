using TunnelFlow.Service.Tun;

namespace TunnelFlow.Tests.Service;

public sealed class WintunPathResolverTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "TunnelFlow-WintunPathResolverTests", Guid.NewGuid().ToString("N"));

    public WintunPathResolverTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task Resolve_PrefersPortableCoreLayoutPath()
    {
        var coreDir = Path.Combine(_tempDir, "core");
        Directory.CreateDirectory(coreDir);

        var corePath = Path.Combine(coreDir, "wintun.dll");
        var flatPath = Path.Combine(_tempDir, "wintun.dll");

        await File.WriteAllTextAsync(corePath, "core");
        await File.WriteAllTextAsync(flatPath, "flat");

        var resolved = WintunPathResolver.Resolve(_tempDir);

        Assert.Equal(corePath, resolved);
    }
}
