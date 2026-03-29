using System.Net;

namespace TunnelFlow.Core.Models;

public record CaptureConfig
{
    public int SocksPort { get; init; }

    public IPAddress SocksAddress { get; init; } = null!;

    public IReadOnlyList<AppRule> Rules { get; init; } = [];

    public IReadOnlyList<string> ExcludedProcessPaths { get; init; } = [];

    public IReadOnlyList<IPAddress> ExcludedDestinations { get; init; } = [];
}
