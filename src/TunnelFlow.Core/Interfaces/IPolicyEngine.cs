using System.Net;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Core;

/// <summary>
/// Evaluates per-flow routing policy (proxy, direct, block) from app rules and global overrides such as QUIC blocking.
/// </summary>
public interface IPolicyEngine
{
    /// <summary>Evaluate what to do with a new flow.</summary>
    PolicyDecision Evaluate(int pid, string processPath, IPEndPoint destination, Protocol protocol);

    /// <summary>Reload rules without restarting capture.</summary>
    void UpdateRules(IReadOnlyList<AppRule> rules);
}
