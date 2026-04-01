using TunnelFlow.Service;
using TunnelFlow.Service.Tun;

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
}
