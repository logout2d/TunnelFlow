using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace TunnelFlow.Capture.TransparentProxy;

/// <summary>
/// Bridge between WinpkFilter (raw TCP redirect) and sing-box (SOCKS5).
/// Accepts raw TCP on <c>relayEndpoint</c>, looks up the original destination
/// from the shared NAT table, performs a SOCKS5 CONNECT to sing-box, and
/// relays data bidirectionally.
/// </summary>
public sealed class LocalRelay : IAsyncDisposable
{
    private readonly IPEndPoint _listenEndpoint;
    private readonly IPEndPoint _socksEndpoint;
    private readonly Func<IReadOnlyDictionary<string, IPEndPoint>> _getNatTable;
    private readonly ILogger<LocalRelay> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionThrottle;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private int _activeConnections;

    private const int BufferSize = 65536;
    private const int MaxConcurrentConnections = 4096;

    public int ActiveConnections => _activeConnections;

    public LocalRelay(
        IPEndPoint relayEndpoint,
        IPEndPoint socksEndpoint,
        Func<IReadOnlyDictionary<string, IPEndPoint>> getNatTable,
        ILogger<LocalRelay> logger)
    {
        _listenEndpoint = relayEndpoint;
        _socksEndpoint = socksEndpoint;
        _getNatTable = getNatTable;
        _logger = logger;
        _connectionThrottle = new SemaphoreSlim(MaxConcurrentConnections, MaxConcurrentConnections);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(_listenEndpoint);
        _listener.Server.NoDelay = true;
        _listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: 512);

        _logger.LogInformation(
            "LocalRelay started on {Endpoint}, forwarding to SOCKS5 {Socks}",
            _listenEndpoint, _socksEndpoint);

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _acceptLoop = AcceptLoopAsync(linked.Token);
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                _ = HandleConnectionAsync(client, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
                await Task.Delay(100, ct);
            }
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
    {
        if (!await _connectionThrottle.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            _logger.LogWarning("Connection throttled, dropping");
            client.Dispose();
            return;
        }

        Interlocked.Increment(ref _activeConnections);
        IPEndPoint? clientEndpoint = null;

        try
        {
            clientEndpoint = client.Client.RemoteEndPoint as IPEndPoint;
            if (clientEndpoint is null)
            {
                _logger.LogWarning("Could not determine client endpoint");
                return;
            }

            string natKey = $"{clientEndpoint.Address}:{clientEndpoint.Port}";
            var natTable = _getNatTable();
            if (!natTable.TryGetValue(natKey, out var originalDest))
            {
                _logger.LogWarning("No NAT entry for {Client}, dropping connection", clientEndpoint);
                return;
            }

            _logger.LogDebug(
                "Relaying {Client} → {Destination} via SOCKS5",
                clientEndpoint, originalDest);

            using var clientStream = client.GetStream();
            await using var socksStream = await Socks5Connector.ConnectAsync(
                _socksEndpoint, originalDest, ct);

            await RelayAsync(clientStream, socksStream, ct);
        }
        catch (Socks5Exception ex)
        {
            _logger.LogWarning("SOCKS5 error for {Client}: {Error}", clientEndpoint, ex.Message);
        }
        catch (IOException) { }
        catch (SocketException) { }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling {Client}", clientEndpoint);
        }
        finally
        {
            client.Dispose();
            Interlocked.Decrement(ref _activeConnections);
            _connectionThrottle.Release();
        }
    }

    private static async Task RelayAsync(
        NetworkStream clientStream,
        NetworkStream socksStream,
        CancellationToken ct)
    {
        using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var clientToSocks = CopyDirectionAsync(clientStream, socksStream, relayCts);
        var socksToClient = CopyDirectionAsync(socksStream, clientStream, relayCts);

        await Task.WhenAny(clientToSocks, socksToClient);
        await relayCts.CancelAsync();

        try { await clientToSocks; } catch { }
        try { await socksToClient; } catch { }
    }

    private static async Task CopyDirectionAsync(
        NetworkStream source,
        NetworkStream destination,
        CancellationTokenSource relayCts)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (!relayCts.Token.IsCancellationRequested)
            {
                int bytesRead = await source.ReadAsync(buffer, relayCts.Token);
                if (bytesRead == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), relayCts.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            await relayCts.CancelAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener?.Stop();

        if (_acceptLoop is not null)
            try { await _acceptLoop; } catch { }

        _cts.Dispose();
        _connectionThrottle.Dispose();
        _listener?.Dispose();

        _logger.LogInformation("LocalRelay stopped");
    }
}
