using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TunnelFlow.Capture.TcpRedirect;

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
    private readonly Func<ConnectionLookupKey, IPEndPoint?>? _lookupOriginalDestination;
    private readonly Func<string, IPEndPoint?> _lookupNat;
    private readonly ILogger<LocalRelay> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _connectionThrottle;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private Task? _statsLoop;
    private int _activeConnections;
    private long _acceptedConnections;
    private long _totalConnections;
    private long _natLookupMisses;
    private long _sniResolved;
    private long _httpHostResolved;
    private long _ipFallback;
    private long _socksConnectSuccess;
    private long _socksConnectFailure;
    private long _selfCheckOk;
    private long _selfCheckFail;

    private const int BufferSize = 65536;
    private const int MaxConcurrentConnections = 4096;

    public int ActiveConnections => _activeConnections;

    public LocalRelay(
        IPEndPoint relayEndpoint,
        IPEndPoint socksEndpoint,
        Func<string, IPEndPoint?> lookupNat,
        Func<ConnectionLookupKey, IPEndPoint?>? lookupOriginalDestination,
        ILogger<LocalRelay> logger)
    {
        _listenEndpoint = relayEndpoint;
        _socksEndpoint = socksEndpoint;
        _lookupOriginalDestination = lookupOriginalDestination;
        _lookupNat = lookupNat;
        _logger = logger;
        _connectionThrottle = new SemaphoreSlim(MaxConcurrentConnections, MaxConcurrentConnections);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener = new TcpListener(_listenEndpoint);
        _listener.Server.NoDelay = true;
        _listener.Server.SetSocketOption(
            SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start(backlog: 512);

        _logger.LogInformation(
            "LocalRelay started on {Endpoint}, forwarding to SOCKS5 {Socks}",
            _listenEndpoint, _socksEndpoint);

        await RunStartupSelfCheckAsync(ct);

        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        _acceptLoop = AcceptLoopAsync(linked.Token);
        _statsLoop = StatsLoopAsync(linked.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                Interlocked.Increment(ref _acceptedConnections);
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

            _logger.LogInformation(
                "Relay accept client={Client} accepted={Accepted} active={Active}",
                clientEndpoint,
                Interlocked.Read(ref _acceptedConnections),
                _activeConnections);

            string natKey = $"{clientEndpoint.Address}:{clientEndpoint.Port}";
            var redirectKey = ConnectionLookupKey.From(clientEndpoint);
            var originalDest = ResolveOriginalDestination(
                clientEndpoint,
                _lookupOriginalDestination,
                _lookupNat,
                out var lookupSource);
            _logger.LogInformation(
                "Relay destination-lookup client={Client} redirectKey={RedirectKey} natKey={NatKey} source={Source} originalDst={OriginalDestination}",
                clientEndpoint,
                redirectKey,
                natKey,
                lookupSource,
                originalDest?.ToString() ?? "<miss>");

            if (originalDest is null)
            {
                Interlocked.Increment(ref _natLookupMisses);
                _logger.LogWarning("No NAT entry for {Client}, dropping connection", clientEndpoint);
                return;
            }

            using var clientStream = client.GetStream();
            var sniffResult = await ProtocolSniffer.SniffAsync(clientStream, ct);
            _logger.LogInformation(
                "Relay sniff client={Client} protocol={Protocol} hasDomain={HasDomain} domain={Domain} buffered={BufferedLength} originalDst={OriginalDestination}",
                clientEndpoint,
                sniffResult.Protocol,
                sniffResult.HasDomain,
                sniffResult.Domain ?? "<none>",
                sniffResult.BufferedLength,
                originalDest);
            await using var socksStream = await ConnectSocksAsync(sniffResult, originalDest, clientEndpoint, ct);

            if (sniffResult.BufferedLength > 0)
            {
                await socksStream.WriteAsync(
                    sniffResult.BufferedData.AsMemory(0, sniffResult.BufferedLength),
                    ct);
            }

            await RelayAsync(clientStream, socksStream, ct);
        }
        catch (Socks5Exception) { }
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

    internal static IPEndPoint? ResolveOriginalDestination(
        IPEndPoint clientEndpoint,
        Func<ConnectionLookupKey, IPEndPoint?>? lookupOriginalDestination,
        Func<string, IPEndPoint?> lookupNat,
        out string source)
    {
        var redirectKey = ConnectionLookupKey.From(clientEndpoint);
        var originalDestination = lookupOriginalDestination?.Invoke(redirectKey);
        if (originalDestination is not null)
        {
            source = "redirect-store";
            return originalDestination;
        }

        string natKey = $"{clientEndpoint.Address}:{clientEndpoint.Port}";
        originalDestination = lookupNat(natKey);
        source = originalDestination is not null ? "nat-fallback" : "miss";
        return originalDestination;
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
                _logger.LogInformation(
                    "Relay name-path client={Client} source=tls-sni domain={Domain} port={Port}",
                    clientEndpoint, sniffResult.Domain, originalDest.Port);
            }
            else if (sniffResult.Protocol == SniffedProtocol.HTTP)
            {
                Interlocked.Increment(ref _httpHostResolved);
                _logger.LogInformation(
                    "Relay name-path client={Client} source=http-host domain={Domain} port={Port}",
                    clientEndpoint, sniffResult.Domain, originalDest.Port);
            }

            _logger.LogInformation(
                "Relay path-select client={Client} mode=domain target={Target}",
                clientEndpoint,
                target);

            try
            {
                var stream = await Socks5Connector.ConnectByDomainAsync(
                    _socksEndpoint,
                    sniffResult.Domain!,
                    originalDest.Port,
                    ct,
                    _logger);
                Interlocked.Increment(ref _socksConnectSuccess);
                _logger.LogInformation(
                    "Relay socks-success client={Client} mode=domain target={Target}",
                    clientEndpoint,
                    target);
                return stream;
            }
            catch
            {
                Interlocked.Increment(ref _socksConnectFailure);
                _logger.LogWarning(
                    "Relay socks-failure client={Client} mode=domain target={Target}",
                    clientEndpoint,
                    target);
                throw;
            }
        }

        Interlocked.Increment(ref _ipFallback);
        _logger.LogInformation(
            "Relay path-select client={Client} mode=ip target={Target}",
            clientEndpoint,
            originalDest);

        try
        {
            var stream = await Socks5Connector.ConnectByIpAsync(_socksEndpoint, originalDest, ct, _logger);
            Interlocked.Increment(ref _socksConnectSuccess);
            _logger.LogInformation(
                "Relay socks-success client={Client} mode=ip target={Target}",
                clientEndpoint,
                originalDest);
            return stream;
        }
        catch
        {
            Interlocked.Increment(ref _socksConnectFailure);
            _logger.LogWarning(
                "Relay socks-failure client={Client} mode=ip target={Target}",
                clientEndpoint,
                originalDest);
            throw;
        }
    }

    private async Task StatsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(ct))
        {
            _logger.LogInformation(
                "LocalRelay stats active={Active} accepted={Accepted} total={Total} natMiss={NatMiss} sni={Sni} http={Http} ipFallback={IpFallback} socksOk={SocksOk} socksFail={SocksFail} selfCheckOk={SelfCheckOk} selfCheckFail={SelfCheckFail}",
                _activeConnections,
                Interlocked.Read(ref _acceptedConnections),
                Interlocked.Read(ref _totalConnections),
                Interlocked.Read(ref _natLookupMisses),
                Interlocked.Read(ref _sniResolved),
                Interlocked.Read(ref _httpHostResolved),
                Interlocked.Read(ref _ipFallback),
                Interlocked.Read(ref _socksConnectSuccess),
                Interlocked.Read(ref _socksConnectFailure),
                Interlocked.Read(ref _selfCheckOk),
                Interlocked.Read(ref _selfCheckFail));
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
            "LocalRelay stopped total={Total} accepted={Accepted} natMiss={NatMiss} sni={Sni} http={Http} ipFallback={IpFallback} socksOk={SocksOk} socksFail={SocksFail} selfCheckOk={SelfCheckOk} selfCheckFail={SelfCheckFail}",
            Interlocked.Read(ref _totalConnections),
            Interlocked.Read(ref _acceptedConnections),
            Interlocked.Read(ref _natLookupMisses),
            Interlocked.Read(ref _sniResolved),
            Interlocked.Read(ref _httpHostResolved),
            Interlocked.Read(ref _ipFallback),
            Interlocked.Read(ref _socksConnectSuccess),
            Interlocked.Read(ref _socksConnectFailure),
            Interlocked.Read(ref _selfCheckOk),
            Interlocked.Read(ref _selfCheckFail));
    }

    private async Task RunStartupSelfCheckAsync(CancellationToken ct)
    {
        if (_listener is null)
            return;

        _logger.LogInformation("LocalRelay self-check start listen={Endpoint}", _listenEndpoint);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            var acceptTask = _listener.AcceptTcpClientAsync(timeoutCts.Token).AsTask();
            using var probe = new TcpClient(AddressFamily.InterNetwork);
            probe.NoDelay = true;
            await probe.ConnectAsync(_listenEndpoint.Address, _listenEndpoint.Port, timeoutCts.Token);

            using var accepted = await acceptTask;
            Interlocked.Increment(ref _selfCheckOk);
            _logger.LogInformation("LocalRelay self-check result=ok listen={Endpoint}", _listenEndpoint);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            Interlocked.Increment(ref _selfCheckFail);
            _logger.LogWarning(ex, "LocalRelay self-check result=fail listen={Endpoint}", _listenEndpoint);
        }
    }
}
