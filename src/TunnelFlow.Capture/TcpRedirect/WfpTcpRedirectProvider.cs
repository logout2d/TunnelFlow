using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class WfpTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly ILogger<WfpTcpRedirectProvider> _logger;
    private readonly IOriginalDestinationStore _destinationStore;

    private volatile WfpRedirectConfig _config = new();
    private volatile bool _started;

    public WfpTcpRedirectProvider(
        IOriginalDestinationStore destinationStore,
        ILogger<WfpTcpRedirectProvider> logger)
    {
        _destinationStore = destinationStore;
        _logger = logger;
    }

    public Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default)
    {
        _config = config;
        _started = true;

        _logger.LogInformation(
            "TCP redirect provider start implementation=wfp-stub useWfpTcpRedirect={UseWfpTcpRedirect} status=placeholder",
            config.UseWfpTcpRedirect);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _started = false;
        _logger.LogInformation("TCP redirect provider stop implementation=wfp-stub");
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
        ProviderStarted = _started,
        ActiveProviderName = nameof(WfpTcpRedirectProvider)
    };
}
