using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;
using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.UI;

public sealed class LocalConfigSnapshotLoaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _configPath;

    public LocalConfigSnapshotLoaderTests()
    {
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
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
    public async Task LoadAsync_ReadsPersistedServiceConfigShape()
    {
        var store = new ConfigStore(_configPath);
        var profileId = Guid.NewGuid();

        await store.SaveAsync(new TunnelFlowConfig
        {
            UseTunMode = true,
            ActiveProfileId = profileId,
            Rules =
            [
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Floorp.exe",
                    DisplayName = "Floorp",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            ],
            Profiles =
            [
                new VlessProfile
                {
                    Id = profileId,
                    Name = "Offline Profile",
                    ServerAddress = "vpn.example.com",
                    ServerPort = 443,
                    UserId = "11111111-1111-1111-1111-111111111111",
                    Network = "tcp",
                    Security = "tls",
                    Flow = "xtls-rprx-vision"
                }
            ]
        });

        var loader = new LocalConfigSnapshotLoader(_configPath);
        var snapshot = await loader.LoadAsync();

        Assert.True(snapshot.UseTunMode);
        Assert.Equal(profileId, snapshot.ActiveProfileId);
        Assert.Single(snapshot.Rules);
        Assert.Single(snapshot.Profiles);
        Assert.Equal(@"C:\Apps\Floorp.exe", snapshot.Rules[0].ExePath);
        Assert.Equal("Offline Profile", snapshot.Profiles[0].Name);
        Assert.Equal("11111111-1111-1111-1111-111111111111", snapshot.Profiles[0].UserId);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_DefaultsToTunMode()
    {
        var loader = new LocalConfigSnapshotLoader(_configPath);

        var snapshot = await loader.LoadAsync();

        Assert.True(snapshot.UseTunMode);
    }

    [Fact]
    public async Task LoadAsync_MissingUseTunModeField_DefaultsToTunMode()
    {
        await File.WriteAllTextAsync(_configPath, """
        {
          "rules": [],
          "profiles": [],
          "activeProfileId": null
        }
        """);

        var loader = new LocalConfigSnapshotLoader(_configPath);
        var snapshot = await loader.LoadAsync();

        Assert.True(snapshot.UseTunMode);
    }
}
