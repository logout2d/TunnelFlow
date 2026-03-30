using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;

namespace TunnelFlow.Tests.Service;

public class ConfigStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly string _configPath;
    private readonly ConfigStore _store;

    public ConfigStoreTests()
    {
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "config.json");
        _store = new ConfigStore(_configPath);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task SaveAndLoad_Roundtrip_PreservesNonCredentialFields()
    {
        var originalUserId = Guid.NewGuid().ToString();
        var config = new TunnelFlowConfig
        {
            SocksPort = 9090,
            StartCaptureOnServiceStart = true,
            ActiveProfileId = Guid.NewGuid(),
            Rules =
            [
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\app\test.exe",
                    DisplayName = "Test App",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            ],
            Profiles =
            [
                new VlessProfile
                {
                    Id = Guid.NewGuid(),
                    Name = "My Profile",
                    ServerAddress = "vpn.example.com",
                    ServerPort = 443,
                    UserId = originalUserId,
                    Flow = "xtls-rprx-vision",
                    Network = "tcp",
                    Security = "tls",
                    Tls = new TlsOptions { Sni = "sni.example.com", AllowInsecure = false }
                }
            ]
        };

        await _store.SaveAsync(config);
        var loaded = await _store.LoadAsync();

        Assert.Equal(config.SocksPort, loaded.SocksPort);
        Assert.Equal(config.StartCaptureOnServiceStart, loaded.StartCaptureOnServiceStart);
        Assert.Equal(config.ActiveProfileId, loaded.ActiveProfileId);
        Assert.Single(loaded.Rules);
        Assert.Equal(config.Rules[0].ExePath, loaded.Rules[0].ExePath);
        Assert.Equal(config.Rules[0].Mode, loaded.Rules[0].Mode);
        Assert.Single(loaded.Profiles);
        Assert.Equal(config.Profiles[0].Name, loaded.Profiles[0].Name);
        Assert.Equal(config.Profiles[0].ServerAddress, loaded.Profiles[0].ServerAddress);
        Assert.Equal(config.Profiles[0].Security, loaded.Profiles[0].Security);
        Assert.Equal(config.Profiles[0].Tls?.Sni, loaded.Profiles[0].Tls?.Sni);
        Assert.Equal(config.Profiles[0].Flow, loaded.Profiles[0].Flow);

        // UserId must survive the roundtrip (decrypted correctly)
        Assert.Equal(originalUserId, loaded.Profiles[0].UserId);
    }

    [Fact]
    public async Task SaveAsync_DoesNotStorePlaintextUserId()
    {
        var knownUserId = "super-secret-uuid-12345";
        var config = new TunnelFlowConfig
        {
            Profiles =
            [
                new VlessProfile
                {
                    Id = Guid.NewGuid(),
                    Name = "Secret",
                    ServerAddress = "vpn.example.com",
                    ServerPort = 443,
                    UserId = knownUserId,
                    Network = "tcp",
                    Security = "tls"
                }
            ]
        };

        await _store.SaveAsync(config);
        var rawJson = await File.ReadAllTextAsync(_configPath);

        Assert.DoesNotContain(knownUserId, rawJson);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaultConfig()
    {
        var nonExistentStore = new ConfigStore(Path.Combine(_tempDir, "does-not-exist.json"));
        var config = await nonExistentStore.LoadAsync();

        Assert.NotNull(config);
        Assert.Empty(config.Rules);
        Assert.Empty(config.Profiles);
        Assert.Null(config.ActiveProfileId);
        Assert.Equal(2080, config.SocksPort);
        Assert.False(config.StartCaptureOnServiceStart);
    }

    [Fact]
    public void EncryptField_Then_DecryptField_Roundtrip()
    {
        const string plaintext = "my-secret-uuid";
        var encrypted = ConfigStore.EncryptField(plaintext);
        var decrypted = ConfigStore.DecryptField(encrypted);
        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, encrypted);
    }
}
