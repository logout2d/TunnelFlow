namespace TunnelFlow.UI.Services;

internal sealed class TrayWindowLifecycle
{
    public bool IsExitRequested { get; private set; }

    public void RequestExit() => IsExitRequested = true;

    public bool ShouldHideOnClose() => !IsExitRequested;
}
