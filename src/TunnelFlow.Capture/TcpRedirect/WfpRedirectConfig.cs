using System.Net;

namespace TunnelFlow.Capture.TcpRedirect;

public sealed record WfpRedirectConfig
{
    public bool UseWfpTcpRedirect { get; init; }

    public TimeSpan RecordTtl { get; init; } = TimeSpan.FromMinutes(2);

    public bool EnableDetailedLogging { get; init; }

    public string? NativeDevicePath { get; init; }

    public string? TestProcessPath { get; init; }

    public IPEndPoint? RelayEndpoint { get; init; }
}
