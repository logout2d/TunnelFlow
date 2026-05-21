using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TunnelFlow.Core.Configuration;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.SingBox;
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
        Assert.False(snapshot.RequiresProtectedConfigMigration);
    }

    [Fact]
    public async Task LoadAsync_AfterServiceSave_KeepsExeNameRuleForSingBoxProcessName()
    {
        var profile = new VlessProfile
        {
            Id = Guid.NewGuid(),
            Name = "Profile",
            ServerAddress = "vpn.example.com",
            ServerPort = 443,
            UserId = "11111111-1111-1111-1111-111111111111",
            Network = "tcp",
            Security = "tls"
        };

        await new ConfigStore(_configPath).SaveAsync(new TunnelFlowConfig
        {
            UseTunMode = true,
            ActiveProfileId = profile.Id,
            Rules =
            [
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = "Discord.exe",
                    DisplayName = "Discord",
                    MatchType = AppRuleMatchType.ExeName,
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            ],
            Profiles = [profile]
        });

        var snapshot = await new LocalConfigSnapshotLoader(_configPath).LoadAsync();

        var rule = Assert.Single(snapshot.Rules);
        Assert.Equal(AppRuleMatchType.ExeName, rule.MatchType);

        var singBoxJson = new SingBoxConfigBuilder().Build(snapshot.Profiles[0], new SingBoxConfig
        {
            UseTunMode = snapshot.UseTunMode,
            Rules = snapshot.Rules,
            BinaryPath = "sing-box.exe",
            ConfigOutputPath = "singbox-config.json",
            LogOutputPath = "singbox.log",
            RestartDelay = TimeSpan.FromSeconds(3),
            MaxRestartAttempts = 5
        });

        using var document = JsonDocument.Parse(singBoxJson);
        var routeRules = document.RootElement.GetProperty("route").GetProperty("rules");
        Assert.Contains(routeRules.EnumerateArray(), routeRule =>
            routeRule.TryGetProperty("process_name", out var processNames) &&
            processNames[0].GetString() == "Discord.exe");
        Assert.DoesNotContain(routeRules.EnumerateArray(), routeRule =>
            routeRule.TryGetProperty("process_path", out var processPaths) &&
            processPaths[0].GetString() == "Discord.exe");
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

        var snapshot = await new LocalConfigSnapshotLoader(_configPath).LoadAsync();

        var rule = Assert.Single(snapshot.Rules);
        Assert.Equal(AppRuleMatchType.ExeName, rule.MatchType);
    }

    [Fact]
    public async Task LoadAsync_ReadsLegacyMachineScopedProtectedUserId()
    {
        await File.WriteAllTextAsync(_configPath, $$"""
        {
          "rules": [],
          "profiles": [
            {
              "id": "{{Guid.NewGuid()}}",
              "name": "Legacy Profile",
              "serverAddress": "vpn.example.com",
              "serverPort": 443,
              "userId": "",
              "encryptedUserId": "{{ProtectLegacy("legacy-shared-secret", DataProtectionScope.LocalMachine)}}",
              "network": "tcp",
              "security": "tls",
              "flow": "",
              "isActive": true
            }
          ],
          "activeProfileId": null,
          "useTunMode": true
        }
        """);

        var loader = new LocalConfigSnapshotLoader(_configPath);
        var snapshot = await loader.LoadAsync();

        Assert.Single(snapshot.Profiles);
        Assert.Equal("legacy-shared-secret", snapshot.Profiles[0].UserId);
        Assert.True(snapshot.RequiresProtectedConfigMigration);
    }

    [Fact]
    public async Task LoadAsync_LegacyCurrentUserProtectedUserId_RewritesToVersionedMachineScopedFormat()
    {
        const string userId = "legacy-current-user-secret";
        await WriteSingleProfileConfigAsync(
            _configPath,
            ProtectLegacy(userId, DataProtectionScope.CurrentUser),
            socksPort: 4040);

        var loader = new LocalConfigSnapshotLoader(_configPath);
        var snapshot = await loader.LoadAsync();

        Assert.Single(snapshot.Profiles);
        Assert.Equal(userId, snapshot.Profiles[0].UserId);
        Assert.True(snapshot.RequiresProtectedConfigMigration);

        var rewrittenProtectedValue = await ReadEncryptedUserIdAsync(_configPath);
        Assert.StartsWith(ProtectedConfigField.MachineDpapiV1Prefix, rewrittenProtectedValue);
        Assert.Equal(userId, ConfigStore.DecryptField(rewrittenProtectedValue));
        Assert.Equal(4040, await ReadIntPropertyAsync(_configPath, "socksPort"));

        var serviceConfig = await new ConfigStore(_configPath).LoadAsync();
        Assert.Single(serviceConfig.Profiles);
        Assert.Equal(userId, serviceConfig.Profiles[0].UserId);
        Assert.Equal(4040, serviceConfig.SocksPort);
    }

    [Fact]
    public async Task LoadAsync_WhenLegacyConfigFallbackRequiresMigration_WritesCurrentSharedConfig()
    {
        var appLocalConfigPath = Path.Combine(_tempDir, "config", "config.json");
        var legacyConfigPath = Path.Combine(_tempDir, "legacy", "config.json");
        const string userId = "legacy-fallback-secret";

        Directory.CreateDirectory(Path.GetDirectoryName(legacyConfigPath)!);
        await WriteSingleProfileConfigAsync(
            legacyConfigPath,
            ProtectLegacy(userId, DataProtectionScope.CurrentUser),
            socksPort: 5050);

        var loader = new LocalConfigSnapshotLoader(appLocalConfigPath, legacyConfigPath);
        var snapshot = await loader.LoadAsync();

        Assert.True(snapshot.RequiresProtectedConfigMigration);
        Assert.Equal(userId, Assert.Single(snapshot.Profiles).UserId);
        Assert.True(File.Exists(appLocalConfigPath));
        Assert.StartsWith(ProtectedConfigField.MachineDpapiV1Prefix, await ReadEncryptedUserIdAsync(appLocalConfigPath));
        Assert.Equal(5050, await ReadIntPropertyAsync(appLocalConfigPath, "socksPort"));
    }

    [Fact]
    public async Task LoadAsync_UnreadableProtectedUserId_ThrowsControlledDiagnostic()
    {
        await WriteSingleProfileConfigAsync(
            _configPath,
            ProtectedConfigField.MachineDpapiV1Prefix + "not-base64",
            socksPort: 2080);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new LocalConfigSnapshotLoader(_configPath).LoadAsync());

        Assert.Contains("Failed to load local config", ex.Message);
        Assert.Contains("profiles[0].encryptedUserId", ex.InnerException?.Message);
        Assert.Contains("could not be decrypted", ex.InnerException?.Message);
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
          "useTunMode": true
        }
        """);

        var loader = new LocalConfigSnapshotLoader(appLocalConfigPath, legacyConfigPath);
        var snapshot = await loader.LoadAsync();

        Assert.True(snapshot.UseTunMode);
    }

    private static string ProtectLegacy(string plaintext, DataProtectionScope scope)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, scope);
        return Convert.ToBase64String(encrypted);
    }

    private static async Task WriteSingleProfileConfigAsync(string configPath, string encryptedUserId, int socksPort)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, $$"""
        {
          "rules": [],
          "profiles": [
            {
              "id": "{{Guid.NewGuid()}}",
              "name": "Legacy Profile",
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
          "socksPort": {{socksPort}},
          "startCaptureOnServiceStart": true,
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

    private static async Task<int> ReadIntPropertyAsync(string configPath, string propertyName)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        return document.RootElement.GetProperty(propertyName).GetInt32();
    }
}
