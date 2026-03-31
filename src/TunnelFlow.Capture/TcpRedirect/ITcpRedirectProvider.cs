using System.Net;

namespace TunnelFlow.Capture.TcpRedirect;

public interface ITcpRedirectProvider
{
    Task StartAsync(WfpRedirectConfig config, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    bool TryGetOriginalDestination(ConnectionLookupKey key, out ConnectionRedirectRecord record);

    TcpRedirectStats GetStats();
}
