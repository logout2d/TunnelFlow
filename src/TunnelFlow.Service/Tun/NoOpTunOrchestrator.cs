using Microsoft.Extensions.Logging;

namespace TunnelFlow.Service.Tun;

public sealed class NoOpTunOrchestrator : ITunOrchestrator
{
    private readonly ILogger<NoOpTunOrchestrator> _logger;

    public NoOpTunOrchestrator(ILogger<NoOpTunOrchestrator> logger) => _logger = logger;

    public bool SupportsActivation => false;

    public Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "TUN orchestrator stub start useTunMode={UseTunMode}",
            config.UseTunMode);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("TUN orchestrator stub stop");
        return Task.CompletedTask;
    }
}
