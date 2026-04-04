using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using TunnelFlow.Core.Models;

namespace TunnelFlow.UI.Services;

public sealed class DirectUrlProfileImportService : IProfileImportService
{
    private readonly Func<Uri, CancellationToken, Task<string>> _fetchTextAsync;

    public DirectUrlProfileImportService(
        HttpClient? httpClient = null,
        Func<Uri, CancellationToken, Task<string>>? fetchTextAsync = null)
    {
        var client = httpClient ?? new HttpClient();
        _fetchTextAsync = fetchTextAsync ?? ((uri, cancellationToken) => client.GetStringAsync(uri, cancellationToken));
    }

    public async Task<ProfileImportResult> ImportProfilesAsync(string url, CancellationToken cancellationToken)
    {
        var requestUri = ValidateAndParseInput(url, out var trimmedInput);
        if (string.Equals(requestUri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileImportResult([ParseSingleProfile(trimmedInput!)], 0);
        }

        var content = await FetchContentAsync(requestUri, cancellationToken);
        return ParseProfileBatch(content, requestUri.ToString());
    }

    public async Task<VlessProfile> ImportFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        var requestUri = ValidateAndParseInput(url, out var trimmedInput);
        if (string.Equals(requestUri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSingleProfile(trimmedInput!);
        }

        var content = await FetchContentAsync(requestUri, cancellationToken);
        return ParseSingleProfile(content);
    }

    internal static VlessProfile ParseSingleProfile(string content)
    {
        var candidate = ExtractSingleSupportedUri(content);
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The URL did not return a supported VLESS profile.");
        }

        var userId = Uri.UnescapeDataString(uri.UserInfo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("The imported VLESS profile is missing a user ID.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0)
        {
            throw new InvalidOperationException("The imported VLESS profile is missing a server address or port.");
        }

        var query = ParseQueryString(uri.Query);
        var security = query.TryGetValue("security", out var securityValue) && !string.IsNullOrWhiteSpace(securityValue)
            ? securityValue.Trim().ToLowerInvariant()
            : "none";
        if (security is not ("none" or "tls" or "reality"))
        {
            throw new InvalidOperationException($"Unsupported VLESS security '{security}'.");
        }

        var network = query.TryGetValue("type", out var typeValue) && !string.IsNullOrWhiteSpace(typeValue)
            ? typeValue.Trim().ToLowerInvariant()
            : "tcp";
        if (network is not ("tcp" or "ws" or "grpc"))
        {
            throw new InvalidOperationException($"Unsupported VLESS transport '{network}'.");
        }

        var name = Uri.UnescapeDataString(uri.Fragment.TrimStart('#')).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = uri.Host;
        }

        query.TryGetValue("sni", out var sni);
        query.TryGetValue("flow", out var flow);
        query.TryGetValue("fp", out var fingerprint);
        if (string.IsNullOrWhiteSpace(fingerprint) && query.TryGetValue("fingerprint", out var alternateFingerprint))
        {
            fingerprint = alternateFingerprint;
        }
        query.TryGetValue("pbk", out var realityPublicKey);
        query.TryGetValue("sid", out var realityShortId);

        return new VlessProfile
        {
            Id = Guid.NewGuid(),
            Name = name,
            ServerAddress = uri.Host,
            ServerPort = uri.Port,
            UserId = userId,
            Flow = flow?.Trim() ?? string.Empty,
            Network = network,
            Security = security,
            Tls = security == "none"
                ? null
                : new TlsOptions
                {
                    Sni = string.IsNullOrWhiteSpace(sni) ? uri.Host : sni.Trim(),
                    AllowInsecure = false,
                    Fingerprint = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint.Trim(),
                    RealityPublicKey = security == "reality" && !string.IsNullOrWhiteSpace(realityPublicKey)
                        ? realityPublicKey.Trim()
                        : null,
                    RealityShortId = security == "reality" && !string.IsNullOrWhiteSpace(realityShortId)
                        ? realityShortId.Trim()
                        : null
                },
            IsActive = false
        };
    }

    internal static ProfileImportResult ParseProfileBatch(string content, string? subscriptionSourceUrl = null)
    {
        var normalizedContent = NormalizeFetchedContent(content);
        var importedProfiles = new List<VlessProfile>();
        var skippedEntries = 0;

        foreach (var line in SplitContentLines(normalizedContent))
        {
            if (IsIgnorableLine(line))
            {
                continue;
            }

            if (!line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            {
                if (LooksLikeUri(line))
                {
                    skippedEntries++;
                }

                continue;
            }

            try
            {
                importedProfiles.Add(ApplySubscriptionMetadata(ParseSingleProfile(line), subscriptionSourceUrl));
            }
            catch (InvalidOperationException)
            {
                skippedEntries++;
            }
        }

        if (importedProfiles.Count > 0)
        {
            return new ProfileImportResult(importedProfiles, skippedEntries);
        }

        throw new InvalidOperationException("The URL did not return any supported VLESS profiles.");
    }

    private static string ExtractSingleSupportedUri(string content)
    {
        var candidates = SplitContentLines(content)
            .Where(line => line.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException("The URL returned multiple profiles. Direct URL import currently supports only one VLESS profile.");
        }

        var trimmed = content.Trim();
        if (trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        throw new InvalidOperationException("The URL did not return a supported VLESS profile.");
    }

    private static Uri ValidateAndParseInput(string url, out string? trimmedInput)
    {
        trimmedInput = url?.Trim();
        if (!Uri.TryCreate(trimmedInput, UriKind.Absolute, out var requestUri))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL, a subscription URL, or a direct vless:// URI.");
        }

        if (string.Equals(requestUri.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
        {
            return requestUri;
        }

        if (requestUri.Scheme != Uri.UriSchemeHttp && requestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL, a subscription URL, or a direct vless:// URI.");
        }

        return requestUri;
    }

    private async Task<string> FetchContentAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        try
        {
            return await _fetchTextAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Import failed. Check the URL and try again.");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Import timed out. Try again.");
        }
    }

    private static string NormalizeFetchedContent(string content)
    {
        var trimmed = content.Trim();
        if (ContainsUriContent(trimmed))
        {
            return trimmed;
        }

        if (TryDecodeBase64Content(trimmed, out var decodedContent) && ContainsUriContent(decodedContent))
        {
            return decodedContent;
        }

        return trimmed;
    }

    private static IEnumerable<string> SplitContentLines(string content) =>
        content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));

    private static bool ContainsUriContent(string content) =>
        SplitContentLines(content).Any(LooksLikeUri) ||
        content.StartsWith("vless://", StringComparison.OrdinalIgnoreCase);

    private static bool TryDecodeBase64Content(string content, out string decodedContent)
    {
        decodedContent = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var normalized = new string(content.Where(character => !char.IsWhiteSpace(character)).ToArray());
        if (normalized.Length == 0 || normalized.Length % 4 != 0)
        {
            return false;
        }

        try
        {
            decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Trim();
            return !string.IsNullOrWhiteSpace(decodedContent);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsIgnorableLine(string line) =>
        line.StartsWith("#", StringComparison.Ordinal) ||
        line.StartsWith("//", StringComparison.Ordinal) ||
        line.StartsWith(";", StringComparison.Ordinal);

    private static bool LooksLikeUri(string line) =>
        Regex.IsMatch(line, "^[A-Za-z][A-Za-z0-9+.-]*://", RegexOptions.CultureInvariant);

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }

    internal static string BuildSubscriptionProfileKey(VlessProfile profile) =>
        string.Join(
            "|",
            profile.UserId.Trim(),
            profile.ServerAddress.Trim().ToLowerInvariant(),
            profile.ServerPort.ToString(),
            profile.Network.Trim().ToLowerInvariant());

    private static VlessProfile ApplySubscriptionMetadata(VlessProfile profile, string? subscriptionSourceUrl)
    {
        if (string.IsNullOrWhiteSpace(subscriptionSourceUrl))
        {
            return profile;
        }

        return profile with
        {
            SubscriptionSourceUrl = subscriptionSourceUrl,
            SubscriptionProfileKey = BuildSubscriptionProfileKey(profile)
        };
    }
}
