namespace TunnelFlow.Service.Tun;

public sealed class TunOrchestrationConfig
{
    public bool UseTunMode { get; init; }

    public string WintunPath { get; init; } = string.Empty;
}
