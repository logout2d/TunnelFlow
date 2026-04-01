namespace TunnelFlow.Service.Tun;

public enum TunnelMode
{
    Legacy,
    Tun
}

public sealed record TunModeSelectionResult(
    bool UseTunModeRequested,
    bool TunPrerequisitesSatisfied,
    bool TunActivationSupported,
    TunnelMode SelectedMode,
    string SelectionReason,
    string WintunPath);

public static class TunModeSelector
{
    public static TunModeSelectionResult Select(
        bool useTunModeRequested,
        bool tunActivationSupported,
        string wintunPath)
    {
        bool tunPrerequisitesSatisfied = File.Exists(wintunPath);

        if (!useTunModeRequested)
        {
            return new TunModeSelectionResult(
                UseTunModeRequested: false,
                TunPrerequisitesSatisfied: tunPrerequisitesSatisfied,
                TunActivationSupported: tunActivationSupported,
                SelectedMode: TunnelMode.Legacy,
                SelectionReason: "tun-not-requested",
                WintunPath: wintunPath);
        }

        if (!tunPrerequisitesSatisfied)
        {
            return new TunModeSelectionResult(
                UseTunModeRequested: true,
                TunPrerequisitesSatisfied: false,
                TunActivationSupported: tunActivationSupported,
                SelectedMode: TunnelMode.Legacy,
                SelectionReason: "wintun-missing",
                WintunPath: wintunPath);
        }

        if (!tunActivationSupported)
        {
            return new TunModeSelectionResult(
                UseTunModeRequested: true,
                TunPrerequisitesSatisfied: true,
                TunActivationSupported: false,
                SelectedMode: TunnelMode.Legacy,
                SelectionReason: "tun-activation-not-supported-yet",
                WintunPath: wintunPath);
        }

        return new TunModeSelectionResult(
            UseTunModeRequested: true,
            TunPrerequisitesSatisfied: true,
            TunActivationSupported: true,
            SelectedMode: TunnelMode.Tun,
            SelectionReason: "tun-selected",
            WintunPath: wintunPath);
    }
}
