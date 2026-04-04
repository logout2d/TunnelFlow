using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using TunnelFlow.Core.Models;

namespace TunnelFlow.UI.Services;

public sealed class LocalConfigSnapshotLoader
{
    public static readonly string DefaultConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TunnelFlow",
        "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _configPath;

    public LocalConfigSnapshotLoader(string? configPath = null)
    {
        _configPath = string.IsNullOrWhiteSpace(configPath) ? DefaultConfigPath : configPath;
    }

    public string ConfigPath => _configPath;

    public async Task<LocalConfigSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_configPath))
        {
            return LocalConfigSnapshot.Empty;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedConfig>(json, JsonOptions)
                            ?? new PersistedConfig();

            return new LocalConfigSnapshot
            {
                Rules = persisted.Rules,
                Profiles = persisted.Profiles.Select(ToVlessProfile).ToList(),
                ActiveProfileId = persisted.ActiveProfileId,
                UseTunMode = persisted.UseTunMode
            };
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException)
        {
            throw new InvalidOperationException($"Failed to load local config from {_configPath}", ex);
        }
    }

    private static VlessProfile ToVlessProfile(PersistedVlessProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        ServerAddress = profile.ServerAddress,
        ServerPort = profile.ServerPort,
        UserId = string.IsNullOrEmpty(profile.EncryptedUserId)
            ? profile.UserId
            : DecryptField(profile.EncryptedUserId),
        Flow = profile.Flow,
        Network = profile.Network,
        Security = profile.Security,
        Tls = profile.Tls,
        SubscriptionSourceUrl = profile.SubscriptionSourceUrl,
        SubscriptionProfileKey = profile.SubscriptionProfileKey,
        IsActive = profile.IsActive
    };

    private static string DecryptField(string base64)
    {
        var encrypted = Convert.FromBase64String(base64);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class PersistedConfig
    {
        [JsonPropertyName("rules")]
        public List<AppRule> Rules { get; set; } = [];

        [JsonPropertyName("profiles")]
        public List<PersistedVlessProfile> Profiles { get; set; } = [];

        [JsonPropertyName("activeProfileId")]
        public Guid? ActiveProfileId { get; set; }

        [JsonPropertyName("useTunMode")]
        public bool UseTunMode { get; set; }
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
        UseTunMode = false
    };

    public IReadOnlyList<AppRule> Rules { get; init; } = Array.Empty<AppRule>();

    public IReadOnlyList<VlessProfile> Profiles { get; init; } = Array.Empty<VlessProfile>();

    public Guid? ActiveProfileId { get; init; }

    public bool UseTunMode { get; init; }
}
