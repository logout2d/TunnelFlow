using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.Configuration;
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
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _configPath;
    private readonly string? _legacyConfigPath;
    private readonly ILogger<ConfigStore>? _logger;

    public ConfigStore() : this(DefaultConfigPath, LegacyConfigPath) { }

    public ConfigStore(string configPath) : this(configPath, legacyConfigPath: null) { }

    public ConfigStore(ILogger<ConfigStore> logger) : this(DefaultConfigPath, LegacyConfigPath, logger) { }

    public ConfigStore(string configPath, string? legacyConfigPath)
        : this(configPath, legacyConfigPath, logger: null)
    {
    }

    private ConfigStore(string configPath, string? legacyConfigPath, ILogger<ConfigStore>? logger)
    {
        _configPath = configPath;
        _legacyConfigPath = legacyConfigPath;
        _logger = logger;
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
                Profiles = persisted.Profiles.Select((profile, index) => ToVlessProfile(profile, configPath, index)).ToList(),
                ActiveProfileId = persisted.ActiveProfileId,
                SocksPort = persisted.SocksPort,
                StartCaptureOnServiceStart = persisted.StartCaptureOnServiceStart,
                UseTunMode = persisted.UseTunMode ?? true
            };
        }
        catch (Exception ex) when (ex is JsonException or ProtectedConfigFieldException)
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
        return ProtectedConfigField.ProtectForSharedConfig(plaintext);
    }

    public static string DecryptField(string protectedValue)
    {
        return ProtectedConfigField.UnprotectFromSharedConfig(protectedValue).Plaintext;
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

    private VlessProfile ToVlessProfile(PersistedVlessProfile p, string configPath, int index)
    {
        var userId = string.Empty;
        if (!string.IsNullOrEmpty(p.EncryptedUserId))
        {
            userId = DecryptProfileUserId(p, configPath, index);
        }

        return new VlessProfile
        {
            Id = p.Id,
            Name = p.Name,
            ServerAddress = p.ServerAddress,
            ServerPort = p.ServerPort,
            UserId = userId,
            Network = p.Network,
            Security = p.Security,
            Flow = p.Flow,
            Tls = p.Tls,
            SubscriptionSourceUrl = p.SubscriptionSourceUrl,
            SubscriptionProfileKey = p.SubscriptionProfileKey,
            SubscriptionMissingFromSource = p.SubscriptionMissingFromSource,
            IsActive = p.IsActive
        };
    }

    private string DecryptProfileUserId(PersistedVlessProfile p, string configPath, int index)
    {
        var fieldPath = $"profiles[{index}].encryptedUserId";
        try
        {
            var result = ProtectedConfigField.UnprotectFromSharedConfig(p.EncryptedUserId);
            if (result.RequiresMigration)
            {
                _logger?.LogInformation(
                    "Config field {FieldPath} for profile {ProfileId} uses legacy protected format {Scheme}; it will be rewritten with the current machine-scoped format on the next save.",
                    fieldPath,
                    p.Id,
                    result.Scheme);
            }

            return result.Plaintext;
        }
        catch (ProtectedConfigFieldException ex)
        {
            _logger?.LogError(
                ex,
                "Unreadable protected config field {FieldPath} for profile {ProfileId} in {ConfigPath}. The shared config secret could not be decrypted in this security context.",
                fieldPath,
                p.Id,
                configPath);

            throw new ProtectedConfigFieldException(
                $"Unreadable protected config field {fieldPath} in {configPath}. The shared config secret could not be decrypted in this security context.",
                ex);
        }
    }

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
