using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class NoOpTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly ILogger<NoOpTcpRedirectProvider> _logger;
    private readonly IOriginalDestinationStore _destinationStore;

    private volatile WfpRedirectConfig _config = new();
    private volatile bool _started;
    private long _redirectRegistrationCount;
    private long _lookupHitCount;
    private long _lookupMissCount;

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

    public void RecordRedirect(ConnectionRedirectRecord record)
    {
        _destinationStore.Add(record);
        Interlocked.Increment(ref _redirectRegistrationCount);
        _logger.LogInformation(
            "TCP redirect metadata-add implementation=no-op key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint}",
            record.LookupKey,
            record.OriginalDestination,
            record.RelayEndpoint);
    }

    public void RemoveRedirect(ConnectionLookupKey key)
    {
        _destinationStore.Remove(key);
        _logger.LogInformation(
            "TCP redirect metadata-remove implementation=no-op key={LookupKey}",
            key);
    }

    public bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record)
    {
        if (_destinationStore.TryGet(key, out record))
        {
            Interlocked.Increment(ref _lookupHitCount);
            _logger.LogInformation(
                "TCP redirect metadata-hit implementation=no-op key={LookupKey} originalDst={OriginalDestination}",
                key,
                record.OriginalDestination);
            return true;
        }

        Interlocked.Increment(ref _lookupMissCount);
        _logger.LogInformation(
            "TCP redirect metadata-miss implementation=no-op key={LookupKey}",
            key);
        record = null!;
        return false;
    }

    public TcpRedirectStats GetStats() => new()
    {
        UseWfpTcpRedirect = _config.UseWfpTcpRedirect,
        ProviderStarted = _started,
        ActiveProviderName = nameof(NoOpTcpRedirectProvider),
        RedirectRegistrationCount = Interlocked.Read(ref _redirectRegistrationCount),
        LookupHitCount = Interlocked.Read(ref _lookupHitCount),
        LookupMissCount = Interlocked.Read(ref _lookupMissCount),
        ActiveRecordCount = _destinationStore.Count
    };
}
