using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class WfpTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly ILogger<WfpTcpRedirectProvider> _logger;
    private readonly IOriginalDestinationStore _destinationStore;

    private volatile WfpRedirectConfig _config = new();
    private volatile bool _started;
    private long _redirectRegistrationCount;
    private long _lookupHitCount;
    private long _lookupMissCount;

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

    public void RecordRedirect(ConnectionRedirectRecord record)
    {
        _destinationStore.Add(record);
        Interlocked.Increment(ref _redirectRegistrationCount);
        _logger.LogInformation(
            "TCP redirect metadata-add implementation=wfp-stub key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint} correlationId={CorrelationId}",
            record.LookupKey,
            record.OriginalDestination,
            record.RelayEndpoint,
            record.CorrelationId);
    }

    public void RemoveRedirect(ConnectionLookupKey key)
    {
        _destinationStore.Remove(key);
        _logger.LogInformation(
            "TCP redirect metadata-remove implementation=wfp-stub key={LookupKey}",
            key);
    }

    public bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record)
    {
        if (_destinationStore.TryGet(key, out record))
        {
            Interlocked.Increment(ref _lookupHitCount);
            _logger.LogInformation(
                "TCP redirect metadata-hit implementation=wfp-stub key={LookupKey} originalDst={OriginalDestination}",
                key,
                record.OriginalDestination);
            return true;
        }

        Interlocked.Increment(ref _lookupMissCount);
        _logger.LogInformation(
            "TCP redirect metadata-miss implementation=wfp-stub key={LookupKey}",
            key);
        record = null!;
        return false;
    }

    public TcpRedirectStats GetStats() => new()
    {
        UseWfpTcpRedirect = _config.UseWfpTcpRedirect,
        ProviderStarted = _started,
        ActiveProviderName = nameof(WfpTcpRedirectProvider),
        RedirectRegistrationCount = Interlocked.Read(ref _redirectRegistrationCount),
        LookupHitCount = Interlocked.Read(ref _lookupHitCount),
        LookupMissCount = Interlocked.Read(ref _lookupMissCount),
        ActiveRecordCount = _destinationStore.Count
    };
}
