using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;
using TunnelFlow.UI.ViewModels;

namespace TunnelFlow.Tests.UI;

public class MainViewModelTests
{
    [Fact]
    public void ApplyStatusPayload_UpdatesTunOrientedStatusSummary()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.ApplyStatusPayload(new StatusPayload
        {
            CaptureRunning = true,
            SingBoxStatus = SingBoxStatus.Running,
            SelectedMode = TunnelStatusMode.Tun,
            SingBoxRunning = true,
            TunnelInterfaceUp = true,
            ActiveProfileId = Guid.NewGuid(),
            ActiveProfileName = "Primary",
            ProxyRuleCount = 2,
            DirectRuleCount = 1,
            BlockRuleCount = 3
        });

        Assert.True(viewModel.CaptureRunning);
        Assert.Equal("Running", viewModel.SingBoxStatus);
        Assert.Equal(TunnelStatusMode.Tun, viewModel.SelectedMode);
        Assert.True(viewModel.SingBoxRunning);
        Assert.True(viewModel.TunnelInterfaceUp);
        Assert.Equal("Primary", viewModel.ActiveProfileName);
        Assert.Equal("TUN", viewModel.ModeSummary);
        Assert.Equal("Running", viewModel.EngineStatusSummary);
        Assert.Equal("Up", viewModel.TunnelStatusSummary);
        Assert.Equal("Proxy 2  Direct 1  Block 3", viewModel.RuleCountsSummary);
    }

    [Fact]
    public void ApplyStatePayload_UsesFallbackProfileTextAndLegacyTunnelSummary()
    {
        using var client = new ServiceClient();
        var viewModel = new MainViewModel(client);

        viewModel.ApplyStatePayload(new StatePayload
        {
            Rules = Array.Empty<AppRule>(),
            Profiles = Array.Empty<VlessProfile>(),
            ActiveProfileId = null,
            ActiveProfileName = null,
            CaptureRunning = false,
            SingBoxStatus = SingBoxStatus.Stopped,
            SelectedMode = TunnelStatusMode.Legacy,
            SingBoxRunning = false,
            TunnelInterfaceUp = false,
            ProxyRuleCount = 0,
            DirectRuleCount = 0,
            BlockRuleCount = 0
        });

        Assert.Equal(TunnelStatusMode.Legacy, viewModel.SelectedMode);
        Assert.Equal("Legacy", viewModel.ModeSummary);
        Assert.Equal("Stopped", viewModel.EngineStatusSummary);
        Assert.Equal("Not enabled", viewModel.TunnelStatusSummary);
        Assert.Equal("None selected", viewModel.ActiveProfileName);
        Assert.Equal("Proxy 0  Direct 0  Block 0", viewModel.RuleCountsSummary);
    }
}
