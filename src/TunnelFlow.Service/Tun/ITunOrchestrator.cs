namespace TunnelFlow.Service.Tun;

public interface ITunOrchestrator
{
    bool SupportsActivation { get; }

    Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
