namespace TunnelFlow.Capture.TcpRedirect;

public sealed record WfpRedirectConfig
{
    public bool UseWfpTcpRedirect { get; init; }

    public TimeSpan RecordTtl { get; init; } = TimeSpan.FromMinutes(2);

    public bool EnableDetailedLogging { get; init; }
}
