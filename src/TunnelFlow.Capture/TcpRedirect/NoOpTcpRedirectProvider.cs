using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class NoOpTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly ILogger<NoOpTcpRedirectProvider> _logger;
    private readonly IOriginalDestinationStore _destinationStore;

    private volatile WfpRedirectConfig _config = new();
    private volatile bool _started;

    public NoOpTcpRedirectProvider(
        IOriginalDestinationStore destinationStore,
        ILogger<NoOpTcpRedirectProvider> logger)
    {
        _destinationStore = destinationStore;
        _logger = logger;
    }

    public Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default)
    {
        _config = config;
        _started = true;

        _logger.LogInformation(
            "TCP redirect provider initialized mode=no-op useWfpTcpRedirect={UseWfpTcpRedirect}",
            config.UseWfpTcpRedirect);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _started = false;
        _logger.LogInformation("TCP redirect provider stopped mode=no-op");
        return Task.CompletedTask;
    }

    public bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record)
    {
        if (_destinationStore.TryGet(key, out record))
            return true;

        record = null!;
        return false;
    }

    public TcpRedirectStats GetStats() => new()
    {
        UseWfpTcpRedirect = _config.UseWfpTcpRedirect,
        ProviderStarted = _started
    };
}
