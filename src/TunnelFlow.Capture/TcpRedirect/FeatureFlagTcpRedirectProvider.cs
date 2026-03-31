using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class FeatureFlagTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly NoOpTcpRedirectProvider _noOpProvider;
    private readonly WfpTcpRedirectProvider _wfpProvider;
    private readonly ILogger<FeatureFlagTcpRedirectProvider> _logger;

    private volatile ITcpRedirectProvider _activeProvider;
    private volatile string _activeProviderName = nameof(NoOpTcpRedirectProvider);
    private volatile WfpRedirectConfig _config = new();

    public FeatureFlagTcpRedirectProvider(
        NoOpTcpRedirectProvider noOpProvider,
        WfpTcpRedirectProvider wfpProvider,
        ILogger<FeatureFlagTcpRedirectProvider> logger)
    {
        _noOpProvider = noOpProvider;
        _wfpProvider = wfpProvider;
        _logger = logger;
        _activeProvider = _noOpProvider;
    }

    public async Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default)
    {
        _config = config;
        _activeProvider = config.UseWfpTcpRedirect ? _wfpProvider : _noOpProvider;
        _activeProviderName = _activeProvider.GetType().Name;

        _logger.LogInformation(
            "TCP redirect provider select useWfpTcpRedirect={UseWfpTcpRedirect} implementation={Implementation}",
            config.UseWfpTcpRedirect,
            _activeProviderName);

        await _activeProvider.StartAsync(config, ct);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "TCP redirect provider lifecycle-stop implementation={Implementation} useWfpTcpRedirect={UseWfpTcpRedirect}",
            _activeProviderName,
            _config.UseWfpTcpRedirect);

        await _activeProvider.StopAsync(ct);
    }

    public void RecordRedirect(ConnectionRedirectRecord record) =>
        _activeProvider.RecordRedirect(record);

    public void RemoveRedirect(ConnectionLookupKey key) =>
        _activeProvider.RemoveRedirect(key);

    public bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record) =>
        _activeProvider.TryGetOriginalDestination(key, out record);

    public TcpRedirectStats GetStats()
    {
        var stats = _activeProvider.GetStats();
        return stats with
        {
            UseWfpTcpRedirect = _config.UseWfpTcpRedirect,
            ActiveProviderName = _activeProviderName
        };
    }
}
