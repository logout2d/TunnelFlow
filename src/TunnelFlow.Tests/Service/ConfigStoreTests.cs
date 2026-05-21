using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TunnelFlow.Core.Configuration;
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
            UseTunMode = true,
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
        Assert.Equal(config.UseTunMode, loaded.UseTunMode);
        Assert.Equal(config.ActiveProfileId, loaded.ActiveProfileId);
        Assert.Single(loaded.Rules);
        Assert.Equal(config.Rules[0].ExePath, loaded.Rules[0].ExePath);
        Assert.Equal(AppRuleMatchType.FullPath, loaded.Rules[0].MatchType);
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
    public async Task SaveAsync_StoresProtectedUserIdWithVersionedMachineScopeFormat()
    {
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
                    UserId = "shared-config-secret",
                    Network = "tcp",
                    Security = "tls"
                }
            ]
        };

        await _store.SaveAsync(config);

        var encryptedUserId = await ReadEncryptedUserIdAsync(_configPath);
        Assert.StartsWith(ProtectedConfigField.MachineDpapiV1Prefix, encryptedUserId);
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
        Assert.True(config.UseTunMode);
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

        var config = await _store.LoadAsync();

        Assert.True(config.UseTunMode);
    }

    [Fact]
    public async Task LoadAsync_RuleMissingMatchType_DefaultsToFullPath()
    {
        await File.WriteAllTextAsync(_configPath, """
        {
          "rules": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "exePath": "C:\\Apps\\Game.exe",
              "displayName": "Game",
              "mode": "proxy",
              "isEnabled": true
            }
          ],
          "profiles": [],
          "activeProfileId": null,
          "useTunMode": true
        }
        """);

        var config = await _store.LoadAsync();

        Assert.Single(config.Rules);
        Assert.Equal(AppRuleMatchType.FullPath, config.Rules[0].MatchType);
    }

    [Fact]
    public async Task SaveAndLoad_ExeNameRule_PreservesMatchType()
    {
        var config = new TunnelFlowConfig
        {
            Rules =
            [
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = "game.exe",
                    DisplayName = "game",
                    MatchType = AppRuleMatchType.ExeName,
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            ]
        };

        await _store.SaveAsync(config);
        var rawJson = await File.ReadAllTextAsync(_configPath);
        var loaded = await _store.LoadAsync();

        Assert.Contains("\"matchType\": \"exeName\"", rawJson);
        Assert.Single(loaded.Rules);
        Assert.Equal("game.exe", loaded.Rules[0].ExePath);
        Assert.Equal(AppRuleMatchType.ExeName, loaded.Rules[0].MatchType);
    }

    [Fact]
    public async Task LoadAsync_PascalCasedMatchType_PreservesExeName()
    {
        await File.WriteAllTextAsync(_configPath, """
        {
          "rules": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "exePath": "Discord.exe",
              "displayName": "Discord",
              "MatchType": "ExeName",
              "mode": "proxy",
              "isEnabled": true
            }
          ],
          "profiles": [],
          "activeProfileId": null,
          "useTunMode": true
        }
        """);

        var config = await _store.LoadAsync();

        Assert.Single(config.Rules);
        Assert.Equal(AppRuleMatchType.ExeName, config.Rules[0].MatchType);
    }

    [Fact]
    public async Task LoadAsync_WhenAppLocalConfigMissing_FallsBackToLegacyConfigPath()
    {
        var appLocalConfigPath = Path.Combine(_tempDir, "config", "config.json");
        var legacyConfigPath = Path.Combine(_tempDir, "legacy", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);

        await File.WriteAllTextAsync(legacyConfigPath, """
        {
          "rules": [],
          "profiles": [],
          "activeProfileId": null,
          "socksPort": 4040,
          "startCaptureOnServiceStart": true,
          "useTunMode": true
        }
        """);

        var store = new ConfigStore(appLocalConfigPath, legacyConfigPath);
        var config = await store.LoadAsync();

        Assert.Equal(4040, config.SocksPort);
        Assert.True(config.StartCaptureOnServiceStart);
        Assert.True(config.UseTunMode);
    }

    [Fact]
    public async Task LoadAsync_LegacyMachineScopedProtectedUserId_Loads()
    {
        const string userId = "legacy-machine-secret";
        await WriteSingleProfileConfigAsync(_configPath, ProtectLegacy(userId, DataProtectionScope.LocalMachine));

        var config = await _store.LoadAsync();

        Assert.Single(config.Profiles);
        Assert.Equal(userId, config.Profiles[0].UserId);
    }

    [Fact]
    public async Task SaveAsync_AfterLegacyLoad_RewritesProtectedUserIdWithVersionedFormat()
    {
        const string userId = "legacy-machine-secret";
        var legacyProtectedValue = ProtectLegacy(userId, DataProtectionScope.LocalMachine);
        await WriteSingleProfileConfigAsync(_configPath, legacyProtectedValue);

        var config = await _store.LoadAsync();
        await _store.SaveAsync(config);

        var rewrittenProtectedValue = await ReadEncryptedUserIdAsync(_configPath);
        Assert.StartsWith(ProtectedConfigField.MachineDpapiV1Prefix, rewrittenProtectedValue);
        Assert.NotEqual(legacyProtectedValue, rewrittenProtectedValue);
        Assert.Equal(userId, ConfigStore.DecryptField(rewrittenProtectedValue));
    }

    [Fact]
    public async Task LoadAsync_UnreadableProtectedUserId_ThrowsControlledDiagnostic()
    {
        await WriteSingleProfileConfigAsync(
            _configPath,
            ProtectedConfigField.MachineDpapiV1Prefix + "not-base64");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _store.LoadAsync());

        Assert.Contains("Failed to load config", ex.Message);
        Assert.Contains("profiles[0].encryptedUserId", ex.InnerException?.Message);
        Assert.Contains("could not be decrypted", ex.InnerException?.Message);
    }

    [Fact]
    public void EncryptField_Then_DecryptField_Roundtrip()
    {
        const string plaintext = "my-secret-uuid";
        var encrypted = ConfigStore.EncryptField(plaintext);
        var decrypted = ConfigStore.DecryptField(encrypted);
        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, encrypted);
        Assert.StartsWith(ProtectedConfigField.MachineDpapiV1Prefix, encrypted);
    }

    private static string ProtectLegacy(string plaintext, DataProtectionScope scope)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, scope);
        return Convert.ToBase64String(encrypted);
    }

    private static async Task WriteSingleProfileConfigAsync(string configPath, string encryptedUserId)
    {
        await File.WriteAllTextAsync(configPath, $$"""
        {
          "rules": [],
          "profiles": [
            {
              "id": "{{Guid.NewGuid()}}",
              "name": "Profile",
              "serverAddress": "vpn.example.com",
              "serverPort": 443,
              "userId": "",
              "encryptedUserId": "{{encryptedUserId}}",
              "network": "tcp",
              "security": "tls",
              "flow": "",
              "isActive": true
            }
          ],
          "activeProfileId": null,
          "socksPort": 2080,
          "startCaptureOnServiceStart": false,
          "useTunMode": true
        }
        """);
    }

    private static async Task<string> ReadEncryptedUserIdAsync(string configPath)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        return document.RootElement
            .GetProperty("profiles")[0]
            .GetProperty("encryptedUserId")
            .GetString()!;
    }
}
