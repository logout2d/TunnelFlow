namespace TunnelFlow.Core.Models;

public record TlsOptions
{
    public string Sni { get; init; } = string.Empty;

    public bool AllowInsecure { get; init; }

    public string? Fingerprint { get; init; }

    /// <summary>Used when profile <c>Security</c> is <c>reality</c>.</summary>
    public string? RealityPublicKey { get; init; }

    /// <summary>Used when profile <c>Security</c> is <c>reality</c>.</summary>
    public string? RealityShortId { get; init; }
}
