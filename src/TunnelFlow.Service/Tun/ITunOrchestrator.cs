namespace TunnelFlow.Service.Tun;

public interface ITunOrchestrator
{
    string ResolvedWintunPath { get; }

    bool SupportsActivation { get; }

    Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
