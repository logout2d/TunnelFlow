using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.Configuration;

public class ConfigStore
{
    private static readonly string DefaultConfigPath = RuntimePaths.Current.CurrentConfigPath;
    private static readonly string LegacyConfigPath = RuntimePaths.Current.LegacyConfigPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _configPath;
    private readonly string? _legacyConfigPath;

    public ConfigStore() : this(DefaultConfigPath, LegacyConfigPath) { }

    public ConfigStore(string configPath) : this(configPath, legacyConfigPath: null) { }

    public ConfigStore(string configPath, string? legacyConfigPath)
    {
        _configPath = configPath;
        _legacyConfigPath = legacyConfigPath;
    }

    public async Task<TunnelFlowConfig> LoadAsync()
    {
        var configPath = ResolveReadConfigPath();
        if (!File.Exists(configPath))
            return new TunnelFlowConfig();

        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            var persisted = JsonSerializer.Deserialize<PersistedConfig>(json, JsonOptions)
                            ?? new PersistedConfig();

            return new TunnelFlowConfig
            {
                Rules = persisted.Rules,
                Profiles = persisted.Profiles.Select(ToVlessProfile).ToList(),
                ActiveProfileId = persisted.ActiveProfileId,
                SocksPort = persisted.SocksPort,
                StartCaptureOnServiceStart = persisted.StartCaptureOnServiceStart,
                UseTunMode = persisted.UseTunMode ?? true
            };
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException)
        {
            throw new InvalidOperationException($"Failed to load config from {configPath}", ex);
        }
    }

    public async Task SaveAsync(TunnelFlowConfig config)
    {
        var dir = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(dir);

        var persisted = new PersistedConfig
        {
            Rules = config.Rules,
            Profiles = config.Profiles.Select(ToPersistedProfile).ToList(),
            ActiveProfileId = config.ActiveProfileId,
            SocksPort = config.SocksPort,
            StartCaptureOnServiceStart = config.StartCaptureOnServiceStart,
            UseTunMode = config.UseTunMode
        };

        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        var tmpPath = _configPath + ".tmp";

        await File.WriteAllTextAsync(tmpPath, json);
        File.Move(tmpPath, _configPath, overwrite: true);
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

    public static string EncryptField(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(encrypted);
    }

    public static string DecryptField(string base64)
    {
        var encrypted = Convert.FromBase64String(base64);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }

    private static PersistedVlessProfile ToPersistedProfile(VlessProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ServerAddress = p.ServerAddress,
        ServerPort = p.ServerPort,
        UserId = string.Empty,
        EncryptedUserId = string.IsNullOrEmpty(p.UserId) ? string.Empty : EncryptField(p.UserId),
        Network = p.Network,
        Security = p.Security,
        Flow = p.Flow,
        Tls = p.Tls,
        SubscriptionSourceUrl = p.SubscriptionSourceUrl,
        SubscriptionProfileKey = p.SubscriptionProfileKey,
        SubscriptionMissingFromSource = p.SubscriptionMissingFromSource,
        IsActive = p.IsActive
    };

    private static VlessProfile ToVlessProfile(PersistedVlessProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        ServerAddress = p.ServerAddress,
        ServerPort = p.ServerPort,
        UserId = string.IsNullOrEmpty(p.EncryptedUserId) ? string.Empty : DecryptField(p.EncryptedUserId),
        Network = p.Network,
        Security = p.Security,
        Flow = p.Flow,
        Tls = p.Tls,
        SubscriptionSourceUrl = p.SubscriptionSourceUrl,
        SubscriptionProfileKey = p.SubscriptionProfileKey,
        SubscriptionMissingFromSource = p.SubscriptionMissingFromSource,
        IsActive = p.IsActive
    };

    // --- Persistence DTOs ---

    private class PersistedConfig
    {
        [JsonPropertyName("rules")]
        public List<AppRule> Rules { get; set; } = [];

        [JsonPropertyName("profiles")]
        public List<PersistedVlessProfile> Profiles { get; set; } = [];

        [JsonPropertyName("activeProfileId")]
        public Guid? ActiveProfileId { get; set; }

        [JsonPropertyName("socksPort")]
        public int SocksPort { get; set; } = 2080;

        [JsonPropertyName("startCaptureOnServiceStart")]
        public bool StartCaptureOnServiceStart { get; set; }

        [JsonPropertyName("useTunMode")]
        public bool? UseTunMode { get; set; }
    }

    private class PersistedVlessProfile
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
