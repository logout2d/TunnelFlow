using System.Net;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.Policy;

public sealed class PolicyEngine : IPolicyEngine
{
    private volatile IReadOnlyList<AppRule> _rules;
    private volatile HashSet<string> _excludedPaths = new(StringComparer.OrdinalIgnoreCase);
    private volatile HashSet<IPAddress> _excludedDestinations = [];
    private readonly ILogger<PolicyEngine>? _logger;

    public PolicyEngine(IReadOnlyList<AppRule> initialRules, ILogger<PolicyEngine>? logger = null)
    {
        _rules = initialRules;
        _logger = logger;
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
        {
            var decision = new PolicyDecision { Action = PolicyAction.Direct, Reason = "self-exclusion" };
            _logger?.LogInformation(
                "Policy self-exclusion pid={Pid} process={ProcessPath} dst={Destination} protocol={Protocol} action={Action} reason={Reason}",
                pid, processPath, destination, protocol, decision.Action, decision.Reason);
            return decision;
        }

        if (_excludedDestinations.Contains(destination.Address))
        {
            var decision = new PolicyDecision { Action = PolicyAction.Direct, Reason = "excluded destination" };
            _logger?.LogInformation(
                "Policy destination-exclusion pid={Pid} process={ProcessPath} dst={Destination} protocol={Protocol} action={Action} reason={Reason}",
                pid, processPath, destination, protocol, decision.Action, decision.Reason);
            return decision;
        }

        var matchedRule = FindMatchingRule(processPath);
        var baseDecision = EvaluateRules(processPath);

        if (matchedRule is not null)
        {
            _logger?.LogInformation(
                "Policy rule-match pid={Pid} process={ProcessPath} dst={Destination} protocol={Protocol} ruleId={RuleId} ruleName={RuleName} mode={RuleMode} action={Action} reason={Reason}",
                pid,
                processPath,
                destination,
                protocol,
                matchedRule.Id,
                matchedRule.DisplayName,
                matchedRule.Mode,
                baseDecision.Action,
                baseDecision.Reason);
        }
        else
        {
            _logger?.LogInformation(
                "Policy rule-no-match pid={Pid} process={ProcessPath} dst={Destination} protocol={Protocol} action={Action} reason={Reason}",
                pid,
                processPath,
                destination,
                protocol,
                baseDecision.Action,
                baseDecision.Reason);
        }

        if (protocol == Protocol.Udp
            && destination.Port == 443
            && baseDecision.Action == PolicyAction.Proxy)
        {
            var decision = new PolicyDecision { Action = PolicyAction.Block, Reason = "QUIC block - forces TCP fallback" };
            _logger?.LogInformation(
                "Policy quic-override pid={Pid} process={ProcessPath} dst={Destination} ruleId={RuleId} ruleName={RuleName} baseAction={BaseAction} finalAction={FinalAction} reason={Reason}",
                pid,
                processPath,
                destination,
                matchedRule?.Id,
                matchedRule?.DisplayName,
                baseDecision.Action,
                decision.Action,
                decision.Reason);
            return decision;
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

    private AppRule? FindMatchingRule(string processPath)
    {
        var rules = _rules;
        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;
            if (!rule.ExePath.Equals(processPath, StringComparison.OrdinalIgnoreCase)) continue;
            return rule;
        }

        return null;
    }
}
