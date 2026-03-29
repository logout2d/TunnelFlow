namespace TunnelFlow.Core.Models;

public record VlessProfile
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string ServerAddress { get; init; } = string.Empty;

    public int ServerPort { get; init; }

    public string UserId { get; init; } = string.Empty;

    public string Network { get; init; } = string.Empty;

    public string Security { get; init; } = string.Empty;

    public TlsOptions? Tls { get; init; }

    public bool IsActive { get; init; }
}
