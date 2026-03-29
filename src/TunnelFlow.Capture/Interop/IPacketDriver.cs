using System.Net;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.Interop;

/// <summary>
/// Low-level abstraction over WinpkFilter NDIS driver.
/// Real implementation wraps WinpkFilter managed API.
/// Fake implementation is used in tests.
/// </summary>
public interface IPacketDriver : IDisposable
{
    /// <summary>Open a handle to the driver. Throws if driver not installed or access denied.</summary>
    void Open();

    /// <summary>Close the driver handle and release all resources.</summary>
    void Close();

    /// <summary>
    /// Start reading packets. Calls <paramref name="onPacket"/> for every intercepted packet.
    /// Runs until cancellation is requested.
    /// </summary>
    Task ReadLoopAsync(Action<PacketInfo> onPacket, CancellationToken ct);

    /// <summary>Redirect the flow identified by <paramref name="flowId"/> to the given endpoint.</summary>
    void RedirectFlow(ulong flowId, IPEndPoint target);

    /// <summary>Drop the flow (block it).</summary>
    void DropFlow(ulong flowId);

    /// <summary>Pass the flow through unmodified.</summary>
    void PassFlow(ulong flowId);
}

public record PacketInfo
{
    public ulong FlowId { get; init; }
    public IPEndPoint Source { get; init; } = null!;
    public IPEndPoint Destination { get; init; } = null!;
    public Protocol Protocol { get; init; }
    public PacketEvent Event { get; init; }
}

public enum PacketEvent
{
    NewFlow,
    Data,
    FlowEnd
}
