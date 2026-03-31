namespace TunnelFlow.Capture.TcpRedirect;

public sealed record TcpRedirectStats
{
    public bool UseWfpTcpRedirect { get; init; }

    public bool ProviderStarted { get; init; }

    public long RedirectRegistrationCount { get; init; }

    public long LookupHitCount { get; init; }

    public long LookupMissCount { get; init; }

    public int ActiveRecordCount { get; init; }
}
