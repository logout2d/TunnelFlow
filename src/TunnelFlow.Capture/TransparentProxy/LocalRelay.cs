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
    private readonly Func<string, IPEndPoint?> _lookupNat;
    private readonly ILogger<LocalRelay> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionThrottle;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _statsLoop;
    private int _activeConnections;
    private long _totalConnections;
    private long _sniResolved;
    private long _httpHostResolved;
    private long _ipFallback;

    private const int BufferSize = 65536;
    private const int MaxConcurrentConnections = 4096;

    public int ActiveConnections => _activeConnections;

    public LocalRelay(
        IPEndPoint relayEndpoint,
        IPEndPoint socksEndpoint,
        Func<string, IPEndPoint?> lookupNat,
        ILogger<LocalRelay> logger)
    {
        _listenEndpoint = relayEndpoint;
        _socksEndpoint = socksEndpoint;
        _lookupNat = lookupNat;
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
        _statsLoop = StatsLoopAsync(linked.Token);
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
        Interlocked.Increment(ref _totalConnections);
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
            var originalDest = _lookupNat(natKey);
            _logger.LogDebug("NAT lookup for key {Key}: {Result}",
                natKey, originalDest?.ToString() ?? "NOT FOUND");

            if (originalDest is null)
            {
                _logger.LogWarning("No NAT entry for {Client}, dropping connection", clientEndpoint);
                return;
            }

            using var clientStream = client.GetStream();
            var sniffResult = await ProtocolSniffer.SniffAsync(clientStream, ct);
            await using var socksStream = await ConnectSocksAsync(sniffResult, originalDest, clientEndpoint, ct);

            if (sniffResult.BufferedLength > 0)
            {
                await socksStream.WriteAsync(
                    sniffResult.BufferedData.AsMemory(0, sniffResult.BufferedLength),
                    ct);
            }

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

    private async Task<NetworkStream> ConnectSocksAsync(
        SniffResult sniffResult,
        IPEndPoint originalDest,
        IPEndPoint? clientEndpoint,
        CancellationToken ct)
    {
        if (sniffResult.HasDomain)
        {
            var target = $"{sniffResult.Domain}:{originalDest.Port}";
            if (sniffResult.Protocol == SniffedProtocol.TLS)
            {
                Interlocked.Increment(ref _sniResolved);
                _logger.LogDebug(
                    "SNI: {Client} → {Domain}:{Port} (TLS SNI)",
                    clientEndpoint, sniffResult.Domain, originalDest.Port);
            }
            else if (sniffResult.Protocol == SniffedProtocol.HTTP)
            {
                Interlocked.Increment(ref _httpHostResolved);
                _logger.LogDebug(
                    "Host: {Client} → {Domain}:{Port} (HTTP Host)",
                    clientEndpoint, sniffResult.Domain, originalDest.Port);
            }

            _logger.LogDebug("Relaying {Client} → {Target} via SOCKS5", clientEndpoint, target);
            return await Socks5Connector.ConnectByDomainAsync(
                _socksEndpoint,
                sniffResult.Domain!,
                originalDest.Port,
                ct);
        }

        Interlocked.Increment(ref _ipFallback);
        _logger.LogDebug("Relaying {Client} → {Target} via SOCKS5", clientEndpoint, originalDest);
        return await Socks5Connector.ConnectByIpAsync(_socksEndpoint, originalDest, ct);
    }

    private async Task StatsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(ct))
        {
            _logger.LogInformation(
                "LocalRelay stats: active={Active} total={Total} sni={Sni} http={Http} ipFallback={IpFallback}",
                _activeConnections,
                Interlocked.Read(ref _totalConnections),
                Interlocked.Read(ref _sniResolved),
                Interlocked.Read(ref _httpHostResolved),
                Interlocked.Read(ref _ipFallback));
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
        if (_statsLoop is not null)
            try { await _statsLoop; } catch { }

        _cts.Dispose();
        _connectionThrottle.Dispose();
        _listener?.Dispose();

        _logger.LogInformation(
            "LocalRelay stopped. Final stats: total={Total} sni={Sni} http={Http} ipFallback={IpFallback}",
            Interlocked.Read(ref _totalConnections),
            Interlocked.Read(ref _sniResolved),
            Interlocked.Read(ref _httpHostResolved),
            Interlocked.Read(ref _ipFallback));
    }
}
