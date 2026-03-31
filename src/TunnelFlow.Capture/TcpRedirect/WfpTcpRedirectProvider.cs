using Microsoft.Extensions.Logging;
using TunnelFlow.Capture.TcpRedirect.Interop;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed class WfpTcpRedirectProvider : ITcpRedirectProvider
{
    private readonly ILogger<WfpTcpRedirectProvider> _logger;
    private readonly IOriginalDestinationStore _destinationStore;
    private readonly WfpNativeSession _nativeSession;

    private volatile WfpRedirectConfig _config = new();
    private volatile bool _started;
    private long _redirectRegistrationCount;
    private long _lookupHitCount;
    private long _lookupMissCount;

    public WfpTcpRedirectProvider(
        IOriginalDestinationStore destinationStore,
        WfpNativeSession nativeSession,
        ILogger<WfpTcpRedirectProvider> logger)
    {
        _destinationStore = destinationStore;
        _nativeSession = nativeSession;
        _logger = logger;
    }

    public async Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default)
    {
        _config = config;
        _started = true;
        _nativeSession.RedirectEventReceived -= OnRedirectEventReceived;
        _nativeSession.RedirectEventReceived += OnRedirectEventReceived;
        await _nativeSession.StartAsync(config, ct);

        _logger.LogInformation(
            "TCP redirect provider start implementation=wfp-provider useWfpTcpRedirect={UseWfpTcpRedirect} nativeMode={NativeMode}",
            config.UseWfpTcpRedirect,
            _nativeSession.Mode);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _started = false;
        _nativeSession.RedirectEventReceived -= OnRedirectEventReceived;
        await _nativeSession.StopAsync(ct);
        _logger.LogInformation("TCP redirect provider stop implementation=wfp-provider");
    }

    public void RecordRedirect(ConnectionRedirectRecord record)
    {
        _destinationStore.Add(record);
        Interlocked.Increment(ref _redirectRegistrationCount);
        _logger.LogInformation(
            "TCP redirect metadata-add implementation=wfp-provider key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint} correlationId={CorrelationId}",
            record.LookupKey,
            record.OriginalDestination,
            record.RelayEndpoint,
            record.CorrelationId);
    }

    public void RemoveRedirect(ConnectionLookupKey key)
    {
        _destinationStore.Remove(key);
        _logger.LogInformation(
            "TCP redirect metadata-remove implementation=wfp-provider key={LookupKey}",
            key);
    }

    public bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record)
    {
        if (_destinationStore.TryGet(key, out record))
        {
            Interlocked.Increment(ref _lookupHitCount);
            _logger.LogInformation(
                "TCP redirect metadata-hit implementation=wfp-provider key={LookupKey} originalDst={OriginalDestination}",
                key,
                record.OriginalDestination);
            return true;
        }

        Interlocked.Increment(ref _lookupMissCount);
        _logger.LogInformation(
            "TCP redirect metadata-miss implementation=wfp-provider key={LookupKey}",
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

    private void OnRedirectEventReceived(object? sender, WfpRedirectEvent redirectEvent) =>
        RecordRedirect(redirectEvent.ToConnectionRedirectRecord(_config.RecordTtl));
}
