namespace TunnelFlow.Capture.TcpRedirect;

public interface IOriginalDestinationStore
{
    int Count { get; }

    void Add(ConnectionRedirectRecord record);

    bool TryGet(ConnectionLookupKey key, out ConnectionRedirectRecord record);

    void Remove(ConnectionLookupKey key);

    int PurgeExpired(DateTime utcNow);
}
