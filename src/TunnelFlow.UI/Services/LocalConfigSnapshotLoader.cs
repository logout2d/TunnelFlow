using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.IO;
using TunnelFlow.Core;
using TunnelFlow.Core.Configuration;
using TunnelFlow.Core.Models;

namespace TunnelFlow.UI.Services;

public sealed class LocalConfigSnapshotLoader
{
    public static readonly string DefaultConfigPath = RuntimePaths.Current.CurrentConfigPath;
    public static readonly string LegacyConfigPath = RuntimePaths.Current.LegacyConfigPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _configPath;
    private readonly string? _legacyConfigPath;

    public LocalConfigSnapshotLoader(string? configPath = null)
        : this(configPath, legacyConfigPath: null)
    {
    }

    public LocalConfigSnapshotLoader(string? configPath, string? legacyConfigPath)
    {
        _configPath = string.IsNullOrWhiteSpace(configPath) ? DefaultConfigPath : configPath;
        _legacyConfigPath = string.IsNullOrWhiteSpace(configPath) ? LegacyConfigPath : legacyConfigPath;
    }

    public string ConfigPath => _configPath;

    public async Task<LocalConfigSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var configPath = ResolveReadConfigPath();
        if (!File.Exists(configPath))
        {
            return LocalConfigSnapshot.Empty;
        }

        try
        {
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedConfig>(json, JsonOptions)
                            ?? new PersistedConfig();
            var migrations = new List<ProtectedFieldMigration>();

            var snapshot = new LocalConfigSnapshot
            {
                Rules = persisted.Rules,
                Profiles = persisted.Profiles.Select((profile, index) => ToVlessProfile(profile, configPath, index, migrations)).ToList(),
                ActiveProfileId = persisted.ActiveProfileId,
                UseTunMode = persisted.UseTunMode ?? true,
                RequiresProtectedConfigMigration = migrations.Count > 0
            };

            if (migrations.Count > 0)
            {
                await RewriteProtectedFieldsAsync(json, configPath, migrations, cancellationToken);
            }

            return snapshot;
        }
        catch (Exception ex) when (ex is JsonException or ProtectedConfigFieldException)
        {
            throw new InvalidOperationException($"Failed to load local config from {configPath}", ex);
        }
    }

    private string ResolveReadConfigPath()
    {
        if (File.Exists(_configPath))
        {
            return _configPath;
        }

        if (!string.IsNullOrWhiteSpace(_legacyConfigPath) && File.Exists(_legacyConfigPath))
        {
            return _legacyConfigPath;
        }

        return _configPath;
    }

    private static VlessProfile ToVlessProfile(
        PersistedVlessProfile profile,
        string configPath,
        int index,
        List<ProtectedFieldMigration> migrations) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        ServerAddress = profile.ServerAddress,
        ServerPort = profile.ServerPort,
        UserId = string.IsNullOrEmpty(profile.EncryptedUserId)
            ? profile.UserId
            : DecryptField(profile.EncryptedUserId, configPath, index, migrations),
        Flow = profile.Flow,
        Network = profile.Network,
        Security = profile.Security,
        Tls = profile.Tls,
        SubscriptionSourceUrl = profile.SubscriptionSourceUrl,
        SubscriptionProfileKey = profile.SubscriptionProfileKey,
        SubscriptionMissingFromSource = profile.SubscriptionMissingFromSource,
        IsActive = profile.IsActive
    };

    private static string DecryptField(
        string protectedValue,
        string configPath,
        int index,
        List<ProtectedFieldMigration> migrations)
    {
        try
        {
            var result = ProtectedConfigField.UnprotectFromSharedConfig(protectedValue);
            if (result.RequiresMigration)
            {
                migrations.Add(new ProtectedFieldMigration(index, result.Plaintext, result.Scheme));
            }

            return result.Plaintext;
        }
        catch (ProtectedConfigFieldException ex)
        {
            throw new ProtectedConfigFieldException(
                $"Unreadable protected config field profiles[{index}].encryptedUserId in {configPath}. The shared config secret could not be decrypted in this security context.",
                ex);
        }
    }

    private async Task RewriteProtectedFieldsAsync(
        string json,
        string sourceConfigPath,
        IReadOnlyList<ProtectedFieldMigration> migrations,
        CancellationToken cancellationToken)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject()
                       ?? throw new JsonException("Config root must be a JSON object.");
            var profiles = root["profiles"]?.AsArray()
                           ?? throw new JsonException("Config profiles field must be a JSON array.");

            foreach (var migration in migrations)
            {
                if (migration.ProfileIndex < 0 || migration.ProfileIndex >= profiles.Count)
                {
                    throw new JsonException($"Config profile index {migration.ProfileIndex} is out of range.");
                }

                var profile = profiles[migration.ProfileIndex]?.AsObject()
                              ?? throw new JsonException($"Config profile at index {migration.ProfileIndex} must be a JSON object.");
                profile["encryptedUserId"] = ProtectedConfigField.ProtectForSharedConfig(migration.Plaintext);
            }

            var dir = Path.GetDirectoryName(_configPath)!;
            Directory.CreateDirectory(dir);

            var tmpPath = _configPath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, root.ToJsonString(JsonOptions), cancellationToken);
            File.Move(tmpPath, _configPath, overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var schemes = string.Join(", ", migrations.Select(m => m.Scheme).Distinct());
            throw new InvalidOperationException(
                $"Protected config migration failed after reading legacy protected values from {sourceConfigPath}. Destination {_configPath}; legacy schemes: {schemes}.",
                ex);
        }
    }

    private sealed record ProtectedFieldMigration(
        int ProfileIndex,
        string Plaintext,
        ProtectedConfigFieldScheme Scheme);

    private sealed class PersistedConfig
    {
        [JsonPropertyName("rules")]
        public List<AppRule> Rules { get; set; } = [];

        [JsonPropertyName("profiles")]
        public List<PersistedVlessProfile> Profiles { get; set; } = [];

        [JsonPropertyName("activeProfileId")]
        public Guid? ActiveProfileId { get; set; }

        [JsonPropertyName("useTunMode")]
        public bool? UseTunMode { get; set; }
    }

    private sealed class PersistedVlessProfile
    {
        [JsonPropertyName("id")]
        public Guid Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("serverAddress")]
        public string ServerAddress { get; set; } = string.Empty;

        [JsonPropertyName("serverPort")]
        public int ServerPort { get; set; }

        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("encryptedUserId")]
        public string EncryptedUserId { get; set; } = string.Empty;

        [JsonPropertyName("network")]
        public string Network { get; set; } = string.Empty;

        [JsonPropertyName("security")]
        public string Security { get; set; } = string.Empty;

        [JsonPropertyName("flow")]
        public string Flow { get; set; } = string.Empty;

        [JsonPropertyName("tls")]
        public TlsOptions? Tls { get; set; }

        [JsonPropertyName("subscriptionSourceUrl")]
        public string? SubscriptionSourceUrl { get; set; }

        [JsonPropertyName("subscriptionProfileKey")]
        public string? SubscriptionProfileKey { get; set; }

        [JsonPropertyName("subscriptionMissingFromSource")]
        public bool SubscriptionMissingFromSource { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }
}

public sealed record LocalConfigSnapshot
{
    public static readonly LocalConfigSnapshot Empty = new()
    {
        Rules = Array.Empty<AppRule>(),
        Profiles = Array.Empty<VlessProfile>(),
        ActiveProfileId = null,
        UseTunMode = true
    };

    public IReadOnlyList<AppRule> Rules { get; init; } = Array.Empty<AppRule>();

    public IReadOnlyList<VlessProfile> Profiles { get; init; } = Array.Empty<VlessProfile>();

    public Guid? ActiveProfileId { get; init; }

    public bool UseTunMode { get; init; } = true;

    public bool RequiresProtectedConfigMigration { get; init; }
}
