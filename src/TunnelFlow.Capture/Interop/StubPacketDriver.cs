using System.Net;
using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.Interop;

/// <summary>
/// Placeholder driver that satisfies <see cref="IPacketDriver"/> without real WinpkFilter.
/// Swap for the real implementation once the SDK is in third_party/.
/// </summary>
public sealed class StubPacketDriver : IPacketDriver
{
    private readonly ILogger<StubPacketDriver> _logger;

    public StubPacketDriver(ILogger<StubPacketDriver> logger) => _logger = logger;

    public void Open() =>
        _logger.LogWarning("Stub packet driver active — no real packet interception");

    public void Close() { }

    public async Task ReadLoopAsync(Action<PacketInfo> onPacket, CancellationToken ct)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Clean shutdown — expected path.
        }
    }

    public void RedirectFlow(ulong flowId, IPEndPoint target) { }
    public void DropFlow(ulong flowId) { }
    public void PassFlow(ulong flowId) { }
    public void Dispose() { }
}
