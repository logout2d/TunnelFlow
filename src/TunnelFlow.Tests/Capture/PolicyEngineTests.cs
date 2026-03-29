using System.Net;
using TunnelFlow.Capture.Policy;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Tests.Capture;

public class PolicyEngineTests
{
    private static AppRule ProxyRule(string path) => new()
    {
        Id = Guid.NewGuid(),
        ExePath = path,
        DisplayName = "test",
        Mode = RuleMode.Proxy,
        IsEnabled = true
    };

    private static AppRule DirectRule(string path) => new()
    {
        Id = Guid.NewGuid(),
        ExePath = path,
        DisplayName = "test",
        Mode = RuleMode.Direct,
        IsEnabled = true
    };

    [Fact]
    public void SelfExclusion_ReturnsDirect_RegardlessOfRules()
    {
        var engine = new PolicyEngine([ProxyRule(@"C:\app\tunnel.exe")]);
        engine.SetHardExclusions([@"C:\app\tunnel.exe"], []);

        var result = engine.Evaluate(
            1, @"C:\app\tunnel.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 80),
            Protocol.Tcp);

        Assert.Equal(PolicyAction.Direct, result.Action);
        Assert.Equal("self-exclusion", result.Reason);
    }

    [Fact]
    public void ExcludedDestination_ReturnsDirect()
    {
        var serverIp = IPAddress.Parse("10.0.0.1");
        var engine = new PolicyEngine([ProxyRule(@"C:\app\browser.exe")]);
        engine.SetHardExclusions([], [serverIp]);

        var result = engine.Evaluate(
            1, @"C:\app\browser.exe",
            new IPEndPoint(serverIp, 443),
            Protocol.Tcp);

        Assert.Equal(PolicyAction.Direct, result.Action);
        Assert.Equal("excluded destination", result.Reason);
    }

    [Fact]
    public void QuicBlock_ReturnsBlock_WhenProxyRuleExistsForProcess()
    {
        var engine = new PolicyEngine([ProxyRule(@"C:\app\browser.exe")]);

        var result = engine.Evaluate(
            1, @"C:\app\browser.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 443),
            Protocol.Udp);

        Assert.Equal(PolicyAction.Block, result.Action);
        Assert.Contains("QUIC", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuicBlock_DoesNotApply_WhenBaseDecisionIsDirect()
    {
        var engine = new PolicyEngine([DirectRule(@"C:\app\browser.exe")]);

        var result = engine.Evaluate(
            1, @"C:\app\browser.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 443),
            Protocol.Udp);

        Assert.Equal(PolicyAction.Direct, result.Action);
    }

    [Fact]
    public void ProxyRule_MatchesByPath_CaseInsensitive()
    {
        var engine = new PolicyEngine([ProxyRule(@"C:\App\Browser.EXE")]);

        var result = engine.Evaluate(
            1, @"c:\app\browser.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 80),
            Protocol.Tcp);

        Assert.Equal(PolicyAction.Proxy, result.Action);
    }

    [Fact]
    public void NoMatchingRule_ReturnsDirect()
    {
        var engine = new PolicyEngine([ProxyRule(@"C:\other\app.exe")]);

        var result = engine.Evaluate(
            1, @"C:\unmatched\app.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 80),
            Protocol.Tcp);

        Assert.Equal(PolicyAction.Direct, result.Action);
    }

    [Fact]
    public void UpdateRules_TakesEffectImmediately()
    {
        var engine = new PolicyEngine([]);

        var before = engine.Evaluate(
            1, @"C:\app\test.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 80),
            Protocol.Tcp);
        Assert.Equal(PolicyAction.Direct, before.Action);

        engine.UpdateRules([ProxyRule(@"C:\app\test.exe")]);

        var after = engine.Evaluate(
            1, @"C:\app\test.exe",
            new IPEndPoint(IPAddress.Parse("1.2.3.4"), 80),
            Protocol.Tcp);
        Assert.Equal(PolicyAction.Proxy, after.Action);
    }
}
