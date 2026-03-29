using System.Net;

namespace TunnelFlow.Capture.ProcessResolver;

/// <summary>
/// Resolves owning process information from live OS connection tables (TCP/UDP).
/// Port-to-PID lookups are never cached because ports are reused quickly.
/// PID-to-path lookups use a short TTL cache.
/// </summary>
public interface IProcessResolver
{
    /// <summary>Try to resolve the process path from a TCP source endpoint. Returns null if not found or process exited.</summary>
    string? ResolveTcpProcess(IPEndPoint localEndpoint);

    /// <summary>Try to resolve the process path from a UDP source endpoint.</summary>
    string? ResolveUdpProcess(IPEndPoint localEndpoint);

    /// <summary>Get the process ID from a TCP source endpoint. Returns null if not found.</summary>
    int? GetTcpPid(IPEndPoint localEndpoint);

    /// <summary>Get the process ID from a UDP source endpoint. Returns null if not found.</summary>
    int? GetUdpPid(IPEndPoint localEndpoint);
}
