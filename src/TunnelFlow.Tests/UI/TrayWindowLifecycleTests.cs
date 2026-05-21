using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.UI;

public sealed class TrayWindowLifecycleTests
{
    [Fact]
    public void ShouldHideOnClose_BeforeExplicitExit_ReturnsTrue()
    {
        var lifecycle = new TrayWindowLifecycle();

        Assert.True(lifecycle.ShouldHideOnClose());
    }

    [Fact]
    public void RequestExit_DisablesHideOnClose()
    {
        var lifecycle = new TrayWindowLifecycle();

        lifecycle.RequestExit();

        Assert.False(lifecycle.ShouldHideOnClose());
        Assert.True(lifecycle.IsExitRequested);
    }
}
