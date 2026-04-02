using TunnelFlow.Service;
using TunnelFlow.Service.Tun;
using TunnelFlow.Core.Models;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Service.Configuration;

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

    [Fact]
    public void BuildStatusSummary_TunMode_BuildsTunOrientedRuntimeSnapshot()
    {
        var activeProfileId = Guid.NewGuid();
        var config = new TunnelFlowConfig
        {
            ActiveProfileId = activeProfileId,
            Profiles =
            {
                new VlessProfile
                {
                    Id = activeProfileId,
                    Name = "Primary",
                    ServerAddress = "example.com",
                    ServerPort = 443,
                    UserId = Guid.NewGuid().ToString()
                }
            },
            Rules =
            {
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Proxy.exe",
                    DisplayName = "Proxy",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Direct.exe",
                    DisplayName = "Direct",
                    Mode = RuleMode.Direct,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Block.exe",
                    DisplayName = "Block",
                    Mode = RuleMode.Block,
                    IsEnabled = true
                },
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"",
                    DisplayName = "Ignored",
                    Mode = RuleMode.Proxy,
                    IsEnabled = true
                }
            }
        };

        var summary = OrchestratorService.BuildStatusSummary(
            config,
            TunnelMode.Tun,
            captureRunning: true,
            tunModeActive: true,
            SingBoxStatus.Running);

        Assert.Equal(TunnelStatusMode.Tun, summary.SelectedMode);
        Assert.True(summary.CaptureRunning);
        Assert.Equal(SingBoxStatus.Running, summary.SingBoxStatus);
        Assert.True(summary.SingBoxRunning);
        Assert.True(summary.TunnelInterfaceUp);
        Assert.Equal(activeProfileId, summary.ActiveProfileId);
        Assert.Equal("Primary", summary.ActiveProfileName);
        Assert.Equal(1, summary.ProxyRuleCount);
        Assert.Equal(1, summary.DirectRuleCount);
        Assert.Equal(1, summary.BlockRuleCount);
    }

    [Fact]
    public void BuildStatusSummary_LegacyMode_KeepsLegacySelectionAndInterfaceDown()
    {
        var config = new TunnelFlowConfig
        {
            Rules =
            {
                new AppRule
                {
                    Id = Guid.NewGuid(),
                    ExePath = @"C:\Apps\Disabled.exe",
                    DisplayName = "Disabled",
                    Mode = RuleMode.Proxy,
                    IsEnabled = false
                }
            }
        };

        var summary = OrchestratorService.BuildStatusSummary(
            config,
            TunnelMode.Legacy,
            captureRunning: false,
            tunModeActive: false,
            SingBoxStatus.Stopped);

        Assert.Equal(TunnelStatusMode.Legacy, summary.SelectedMode);
        Assert.False(summary.CaptureRunning);
        Assert.Equal(SingBoxStatus.Stopped, summary.SingBoxStatus);
        Assert.False(summary.SingBoxRunning);
        Assert.False(summary.TunnelInterfaceUp);
        Assert.Null(summary.ActiveProfileId);
        Assert.Null(summary.ActiveProfileName);
        Assert.Equal(0, summary.ProxyRuleCount);
        Assert.Equal(0, summary.DirectRuleCount);
        Assert.Equal(0, summary.BlockRuleCount);
    }
}
