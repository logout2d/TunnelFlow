namespace TunnelFlow.Core.Models;

public record TlsOptions
{
    public string Sni { get; init; } = string.Empty;

    public bool AllowInsecure { get; init; }

    public string? Fingerprint { get; init; }
}
