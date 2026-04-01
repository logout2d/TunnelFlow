using TunnelFlow.Service;
using TunnelFlow.Service.Tun;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Tests.Service;

public class OrchestratorServiceTests
{
    [Fact]
    public void BuildRuntimePlan_LegacyMode_EnablesLegacyCapturePath()
    {
        var plan = OrchestratorService.BuildRuntimePlan(TunnelMode.Legacy);

        Assert.Equal(TunnelMode.Legacy, plan.SelectedMode);
        Assert.True(plan.LegacyCaptureEnabled);
        Assert.True(plan.LocalRelayEnabled);
        Assert.True(plan.WinpkFilterEnabled);
    }

    [Fact]
    public void BuildRuntimePlan_TunMode_DisablesLegacyCapturePath()
    {
        var plan = OrchestratorService.BuildRuntimePlan(TunnelMode.Tun);

        Assert.Equal(TunnelMode.Tun, plan.SelectedMode);
        Assert.False(plan.LegacyCaptureEnabled);
        Assert.False(plan.LocalRelayEnabled);
        Assert.False(plan.WinpkFilterEnabled);
    }

    [Fact]
    public void BuildTunPolicySummaries_MapsProxyDirectAndBlockRules()
    {
        var rules = new[]
        {
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\ProxyMe.exe",
                DisplayName = "ProxyMe",
                Mode = RuleMode.Proxy,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\DirectMe.exe",
                DisplayName = "DirectMe",
                Mode = RuleMode.Direct,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\BlockMe.exe",
                DisplayName = "BlockMe",
                Mode = RuleMode.Block,
                IsEnabled = true
            },
            new AppRule
            {
                Id = Guid.NewGuid(),
                ExePath = @"C:\Apps\Disabled.exe",
                DisplayName = "Disabled",
                Mode = RuleMode.Proxy,
                IsEnabled = false
            }
        };

        var summaries = OrchestratorService.BuildTunPolicySummaries(rules);

        Assert.Equal(3, summaries.Count);
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\ProxyMe.exe" &&
            summary.RuleMode == RuleMode.Proxy &&
            summary.MappedAction == "route" &&
            summary.MappedOutbound == "vless-out");
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\DirectMe.exe" &&
            summary.RuleMode == RuleMode.Direct &&
            summary.MappedAction == "route" &&
            summary.MappedOutbound == "direct");
        Assert.Contains(summaries, summary =>
            summary.AppPath == @"C:\Apps\BlockMe.exe" &&
            summary.RuleMode == RuleMode.Block &&
            summary.MappedAction == "reject" &&
            summary.MappedOutbound is null);
    }
}
