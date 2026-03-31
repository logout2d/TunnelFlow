namespace TunnelFlow.Capture.TcpRedirect.Interop;

public sealed class WfpNativeInterop
{
    public Task<WfpNativeSessionHandle> OpenSessionAsync(
        WfpRedirectConfig config,
        CancellationToken ct = default) =>
        Task.FromResult(new WfpNativeSessionHandle(Guid.NewGuid()));

    public Task CloseSessionAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<WfpRedirectEvent?> TryReadRedirectEventAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct = default) =>
        Task.FromResult<WfpRedirectEvent?>(null);
}

public readonly record struct WfpNativeSessionHandle(Guid Id);
