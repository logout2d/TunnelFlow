using System.Net;

namespace TunnelFlow.Capture.TcpRedirect;

public readonly record struct ConnectionLookupKey(IPAddress ClientAddress, int ClientPort)
{
    public override string ToString() => $"{ClientAddress}:{ClientPort}";

    public static ConnectionLookupKey From(IPEndPoint endpoint) =>
        new(endpoint.Address, endpoint.Port);
}
