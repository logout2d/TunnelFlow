using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TcpRedirect.Interop;

public sealed class WfpNativeSession
{
    private readonly WfpNativeInterop _interop;
    private readonly ILogger<WfpNativeSession> _logger;
    private readonly TimeSpan _eventPumpInterval;

    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private WfpNativeSessionHandle _handle = WfpNativeSessionHandle.CreateStub();
    private volatile bool _started;

    public event EventHandler<WfpRedirectEvent>? RedirectEventReceived;

    public bool IsStarted => _started;

    public WfpNativeSessionMode Mode => _handle.Mode;

    public WfpNativeSession(
        WfpNativeInterop interop,
        ILogger<WfpNativeSession> logger,
        TimeSpan? eventPumpInterval = null)
    {
        _interop = interop;
        _logger = logger;
        _eventPumpInterval = eventPumpInterval ?? TimeSpan.FromMilliseconds(100);
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
            "WFP native session start useWfpTcpRedirect={UseWfpTcpRedirect} sessionId={SessionId} mode={Mode}",
            config.UseWfpTcpRedirect,
            _handle.Id,
            _handle.Mode);
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
            "WFP native session stop sessionId={SessionId} mode={Mode}",
            _handle.Id,
            _handle.Mode);
    }

    internal async Task PublishSyntheticEventAsync(
        WfpRedirectEvent redirectEvent,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "WFP native session synthetic-event key={LookupKey} originalDst={OriginalDestination} relay={RelayEndpoint} mode={Mode}",
            redirectEvent.LookupKey,
            redirectEvent.OriginalDestination,
            redirectEvent.RelayEndpoint,
            _handle.Mode);

        if (_handle.Mode == WfpNativeSessionMode.Helper)
        {
            await _interop.SendSyntheticRedirectEventAsync(_handle, redirectEvent, ct);
            return;
        }

        RedirectEventReceived?.Invoke(this, redirectEvent);
    }

    private async Task PumpEventsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_eventPumpInterval);

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
