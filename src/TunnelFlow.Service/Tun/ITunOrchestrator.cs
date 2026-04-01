namespace TunnelFlow.Service.Tun;

public interface ITunOrchestrator
{
    Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
