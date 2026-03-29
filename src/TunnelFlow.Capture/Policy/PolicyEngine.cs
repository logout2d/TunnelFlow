using System.Net;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.Policy;

public sealed class PolicyEngine : IPolicyEngine
{
    private volatile IReadOnlyList<AppRule> _rules;
    private volatile HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private volatile HashSet<IPAddress> _excludedDestinations = [];

    public PolicyEngine(IReadOnlyList<AppRule> initialRules)
    {
        _rules = initialRules;
    }

    public void SetHardExclusions(
        IReadOnlyList<string> processPathExclusions,
        IReadOnlyList<IPAddress> destinationExclusions)
    {
        _excludedPaths = new HashSet<string>(processPathExclusions, StringComparer.OrdinalIgnoreCase);
        _excludedDestinations = new HashSet<IPAddress>(destinationExclusions);
    }

    public void UpdateRules(IReadOnlyList<AppRule> rules) => _rules = rules;

    public PolicyDecision Evaluate(int pid, string processPath, IPEndPoint destination, Protocol protocol)
    {
        if (_excludedPaths.Contains(processPath))
            return new PolicyDecision { Action = PolicyAction.Direct, Reason = "self-exclusion" };

        if (_excludedDestinations.Contains(destination.Address))
            return new PolicyDecision { Action = PolicyAction.Direct, Reason = "excluded destination" };

        var baseDecision = EvaluateRules(processPath);

        // QUIC block: override Proxy → Block for UDP port 443 (forces TCP fallback).
        if (protocol == Protocol.Udp
            && destination.Port == 443
            && baseDecision.Action == PolicyAction.Proxy)
        {
            return new PolicyDecision { Action = PolicyAction.Block, Reason = "QUIC block — forces TCP fallback" };
        }

        return baseDecision;
    }

    private PolicyDecision EvaluateRules(string processPath)
    {
        var rules = _rules;
        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;
            if (!rule.ExePath.Equals(processPath, StringComparison.OrdinalIgnoreCase)) continue;

            return rule.Mode switch
            {
                RuleMode.Proxy => new PolicyDecision { Action = PolicyAction.Proxy, Reason = "rule: proxy" },
                RuleMode.Direct => new PolicyDecision { Action = PolicyAction.Direct, Reason = "rule: direct" },
                RuleMode.Block => new PolicyDecision { Action = PolicyAction.Block, Reason = "rule: block" },
                _ => new PolicyDecision { Action = PolicyAction.Direct, Reason = "unknown rule mode" }
            };
        }

        return new PolicyDecision { Action = PolicyAction.Direct, Reason = "no matching rule" };
    }
}
