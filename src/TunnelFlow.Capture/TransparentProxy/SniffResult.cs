namespace TunnelFlow.Capture.TransparentProxy;

public sealed class SniffResult
{
    public string? Domain { get; init; }
    public SniffedProtocol Protocol { get; init; }
    public required byte[] BufferedData { get; init; }
    public int BufferedLength { get; init; }
    public bool HasDomain => !string.IsNullOrEmpty(Domain);
}

public enum SniffedProtocol
{
    Unknown,
    TLS,
    HTTP
}
