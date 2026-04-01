using TunnelFlow.Service.Tun;

namespace TunnelFlow.Tests.Service;

public class TunModeSelectorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public TunModeSelectorTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Select_UseTunModeFalse_KeepsLegacyMode()
    {
        var wintunPath = Path.Combine(_tempDir, "wintun.dll");
        File.WriteAllText(wintunPath, "stub");

        var selection = TunModeSelector.Select(
            useTunModeRequested: false,
            tunActivationSupported: true,
            wintunPath: wintunPath);

        Assert.False(selection.UseTunModeRequested);
        Assert.Equal(TunnelMode.Legacy, selection.SelectedMode);
        Assert.Equal("tun-not-requested", selection.SelectionReason);
    }

    [Fact]
    public void Select_UseTunModeTrue_WithMissingWintun_FallsBackToLegacy()
    {
        var wintunPath = Path.Combine(_tempDir, "missing-wintun.dll");

        var selection = TunModeSelector.Select(
            useTunModeRequested: true,
            tunActivationSupported: true,
            wintunPath: wintunPath);

        Assert.True(selection.UseTunModeRequested);
        Assert.False(selection.TunPrerequisitesSatisfied);
        Assert.Equal(TunnelMode.Legacy, selection.SelectedMode);
        Assert.Equal("wintun-missing", selection.SelectionReason);
    }

    [Fact]
    public void Select_UseTunModeTrue_WithPrerequisitesButNoActivationSupport_FallsBackToLegacy()
    {
        var wintunPath = Path.Combine(_tempDir, "wintun.dll");
        File.WriteAllText(wintunPath, "stub");

        var selection = TunModeSelector.Select(
            useTunModeRequested: true,
            tunActivationSupported: false,
            wintunPath: wintunPath);

        Assert.True(selection.TunPrerequisitesSatisfied);
        Assert.False(selection.TunActivationSupported);
        Assert.Equal(TunnelMode.Legacy, selection.SelectedMode);
        Assert.Equal("tun-activation-not-supported-yet", selection.SelectionReason);
    }

    [Fact]
    public void Select_UseTunModeTrue_WithPrerequisitesAndActivationSupport_SelectsTun()
    {
        var wintunPath = Path.Combine(_tempDir, "wintun.dll");
        File.WriteAllText(wintunPath, "stub");

        var selection = TunModeSelector.Select(
            useTunModeRequested: true,
            tunActivationSupported: true,
            wintunPath: wintunPath);

        Assert.True(selection.TunPrerequisitesSatisfied);
        Assert.True(selection.TunActivationSupported);
        Assert.Equal(TunnelMode.Tun, selection.SelectedMode);
        Assert.Equal("tun-selected", selection.SelectionReason);
    }
}
