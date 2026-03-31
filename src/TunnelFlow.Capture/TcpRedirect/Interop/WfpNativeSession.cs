using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect.Interop;

public sealed class WfpNativeSession
{
    private readonly WfpNativeInterop _interop;
    private readonly ILogger<WfpNativeSession> _logger;

    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private WfpNativeSessionHandle _handle;
    private volatile bool _started;

    public event EventHandler<WfpRedirectEvent>? RedirectEventReceived;

    public bool IsStarted => _started;

    public WfpNativeSession(
        WfpNativeInterop interop,
        ILogger<WfpNativeSession> logger)
    {
        _interop = interop;
        _logger = logger;
    }

    public async Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default)
    {
        if (_started)
            return;

        _handle = await _interop.OpenSessionAsync(config, ct);
        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pumpTask = PumpEventsAsync(_pumpCts.Token);
        _started = true;

        _logger.LogInformation(
            "WFP native session start useWfpTcpRedirect={UseWfpTcpRedirect} sessionId={SessionId}",
            config.UseWfpTcpRedirect,
            _handle.Id);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_started)
            return;

        _pumpCts?.Cancel();

        if (_pumpTask is not null)
        {
            try { await _pumpTask; } catch (OperationCanceledException) { }
        }

        await _interop.CloseSessionAsync(_handle, ct);
        _pumpCts?.Dispose();
        _pumpCts = null;
        _pumpTask = null;
        _started = false;

        _logger.LogInformation(
            "WFP native session stop sessionId={SessionId}",
            _handle.Id);
    }

    internal void PublishSyntheticEvent(WfpRedirectEvent redirectEvent)
    {
        _logger.LogInformation(
            "WFP native session synthetic-event key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint}",
            redirectEvent.LookupKey,
            redirectEvent.OriginalDestination,
            redirectEvent.RelayEndpoint);
        RedirectEventReceived?.Invoke(this, redirectEvent);
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(ct))
        {
            var redirectEvent = await _interop.TryReadRedirectEventAsync(_handle, ct);
            if (redirectEvent is null)
                continue;

            _logger.LogInformation(
                "WFP native session event key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint} correlationId={CorrelationId}",
                redirectEvent.LookupKey,
                redirectEvent.OriginalDestination,
                redirectEvent.RelayEndpoint,
                redirectEvent.CorrelationId);
            RedirectEventReceived?.Invoke(this, redirectEvent);
        }
    }
}
