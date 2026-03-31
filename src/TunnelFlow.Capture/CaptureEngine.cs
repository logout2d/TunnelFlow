using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.Extensions.Logging;
using TunnelFlow.Capture.Interop;
using TunnelFlow.Capture.Policy;
using TunnelFlow.Capture.ProcessResolver;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Capture.TransparentProxy;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture;

public sealed class CaptureEngine : ICaptureEngine
{
    private const int RelayPort = 2070;

    private readonly IPacketDriver _driver;
    private readonly IProcessResolver _processResolver;
    private readonly ISessionRegistry _registry;
    private readonly IPolicyEngine _policyEngine;
    private readonly ITcpRedirectProvider? _tcpRedirectProvider;
    private readonly ILogger<CaptureEngine> _logger;
    private readonly ILoggerFactory? _loggerFactory;

    private CancellationTokenSource? _cts;
    private Task? _readLoopTask;
    private Task? _purgeLoopTask;
    private IPEndPoint? _socksEndpoint;
    private LocalRelay? _localRelay;
    private bool _disposed;

    public event EventHandler<SessionEntry>? SessionCreated;
    public event EventHandler<SessionEntry>? SessionClosed;
    public event EventHandler<CaptureError>? ErrorOccurred;

    public CaptureEngine(
        IPacketDriver driver,
        IProcessResolver processResolver,
        ISessionRegistry registry,
        IPolicyEngine policyEngine,
        ITcpRedirectProvider? tcpRedirectProvider,
        ILogger<CaptureEngine> logger,
        ILoggerFactory? loggerFactory = null)
    {
        _driver = driver;
        _processResolver = processResolver;
        _registry = registry;
        _policyEngine = policyEngine;
        _tcpRedirectProvider = tcpRedirectProvider;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public Task StartAsync(CaptureConfig config, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _driver.Open();

        if (_policyEngine is PolicyEngine pe)
        {
            pe.SetHardExclusions(config.ExcludedProcessPaths, config.ExcludedDestinations);
        }
        _policyEngine.UpdateRules(config.Rules);

        _socksEndpoint = new IPEndPoint(config.SocksAddress, config.SocksPort);
        var relayAddress = SelectRelayListenAddress();
        var relayEndpoint = new IPEndPoint(relayAddress, RelayPort);
        _logger.LogInformation(
            "Capture relay endpoint selected address={RelayAddress} port={RelayPort}",
            relayAddress,
            RelayPort);

        if (_driver is WinpkFilterPacketDriver wpfDriver)
        {
            var includedPaths = config.Rules
                .Where(r => r.Mode == RuleMode.Proxy && r.IsEnabled)
                .Select(r => r.ExePath)
                .ToList();

            wpfDriver.Configure(
                _socksEndpoint,
                relayEndpoint,
                includedPaths,
                config.ExcludedProcessPaths.ToList());

            if (_loggerFactory is not null)
            {
                _localRelay = new LocalRelay(
                    relayEndpoint,
                    _socksEndpoint,
                    key => wpfDriver.LookupNat(key),
                    key => TryLookupOriginalDestination(key),
                    _loggerFactory.CreateLogger<LocalRelay>());
                _ = _localRelay.StartAsync(_cts.Token);
            }
        }

        _readLoopTask = RunReadLoopAsync(_cts.Token);
        _purgeLoopTask = RunPurgeLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    private IPEndPoint? TryLookupOriginalDestination(ConnectionLookupKey key)
    {
        if (_tcpRedirectProvider is not null &&
            _tcpRedirectProvider.TryGetOriginalDestination(key, out var record))
        {
            return record.OriginalDestination;
        }

        return null;
    }

    internal static IPAddress SelectRelayListenAddress() =>
        SelectRelayListenAddress(GetCandidateRelayAddresses());

    internal static IPAddress SelectRelayListenAddress(IEnumerable<IPAddress> candidates)
    {
        foreach (var address in candidates)
        {
            if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                continue;

            if (IPAddress.IsLoopback(address))
                continue;

            var bytes = address.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254)
                continue;

            if (address.Equals(IPAddress.Any))
                continue;

            return address;
        }

        throw new InvalidOperationException(
            "No non-loopback IPv4 address found for LocalRelay. " +
            "TunnelFlow requires an active local IPv4 host address for redirected relay traffic.");
    }

    private static IEnumerable<IPAddress> GetCandidateRelayAddresses()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
                yield return unicast.Address;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_cts is null or { IsCancellationRequested: true })
            return;

        _cts.Cancel();

        if (_localRelay is not null)
        {
            await _localRelay.DisposeAsync();
            _localRelay = null;
        }

        try
        {
            var pending = Task.WhenAll(
                _readLoopTask ?? Task.CompletedTask,
                _purgeLoopTask ?? Task.CompletedTask);

            await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException)
        {
            _logger.LogWarning("Capture tasks did not stop within 2 s timeout");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error waiting for capture tasks to complete");
        }

        _driver.Close();
    }

    public IReadOnlyList<SessionEntry> GetActiveSessions() =>
        _registry.GetAll().Where(s => s.State == SessionState.Active).ToList();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_cts is { IsCancellationRequested: false })
        {
            StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        _cts?.Dispose();
    }

    private async Task RunReadLoopAsync(CancellationToken ct)
    {
        try
        {
            await _driver.ReadLoopAsync(OnPacket, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Read loop failed");
            ErrorOccurred?.Invoke(this, new CaptureError { Message = ex.Message, Code = "READ_LOOP" });
        }
    }

    private async Task RunPurgeLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                _registry.PurgeExpiredUdp(TimeSpan.FromSeconds(30));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void OnPacket(PacketInfo packet)
    {
        try
        {
            switch (packet.Event)
            {
                case PacketEvent.NewFlow:
                    HandleNewFlow(packet);
                    break;
                case PacketEvent.Data:
                    _registry.UpdateActivity(packet.FlowId);
                    break;
                case PacketEvent.FlowEnd:
                    HandleFlowEnd(packet);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing packet for flow {FlowId}", packet.FlowId);
            ErrorOccurred?.Invoke(this, new CaptureError { Message = ex.Message, Code = "PACKET_PROCESSING" });
        }
    }

    private void HandleNewFlow(PacketInfo packet)
    {
        int? pid;
        string? processPath;

        if (packet.Protocol == Protocol.Tcp)
        {
            (pid, processPath) = ResolveWithRetry(packet.Source, isTcp: true);
        }
        else
        {
            pid = _processResolver.GetUdpPid(packet.Source);
            processPath = _processResolver.ResolveUdpProcess(packet.Source);
        }

        if (pid is null || processPath is null)
        {
            _driver.PassFlow(packet.FlowId);
            _logger.LogInformation(
                "Capture flow unresolved flowId={FlowId} protocol={Protocol} src={Source} dst={Destination} pid={Pid} process={ProcessPath} action={Action} reason={Reason}",
                packet.FlowId,
                packet.Protocol,
                packet.Source,
                packet.Destination,
                pid,
                processPath ?? "<unresolved>",
                PolicyAction.Direct,
                "unresolved process");
            return;
        }

        var decision = _policyEngine.Evaluate(pid.Value, processPath, packet.Destination, packet.Protocol);
        _logger.LogInformation(
            "Capture flow decision flowId={FlowId} protocol={Protocol} src={Source} dst={Destination} pid={Pid} process={ProcessPath} action={Action} reason={Reason}",
            packet.FlowId,
            packet.Protocol,
            packet.Source,
            packet.Destination,
            pid.Value,
            processPath,
            decision.Action,
            decision.Reason ?? "<none>");

        switch (decision.Action)
        {
            case PolicyAction.Proxy:
                _driver.RedirectFlow(packet.FlowId, _socksEndpoint!);

                if (_driver is WinpkFilterPacketDriver wpf)
                    wpf.AddNatEntry(packet.Source, packet.Destination);

                var entry = new SessionEntry
                {
                    FlowId = packet.FlowId,
                    ProcessId = pid.Value,
                    ProcessPath = processPath,
                    Protocol = packet.Protocol,
                    OriginalSource = packet.Source,
                    OriginalDestination = packet.Destination,
                    Decision = decision,
                    CreatedAt = DateTime.UtcNow,
                    LastActivityAt = DateTime.UtcNow,
                    State = SessionState.Active
                };
                _registry.Add(entry);
                SessionCreated?.Invoke(this, entry);
                break;

            case PolicyAction.Block:
                _driver.DropFlow(packet.FlowId);
                break;

            case PolicyAction.Direct:
            default:
                _driver.PassFlow(packet.FlowId);
                break;
        }
    }

    private (int? pid, string? path) ResolveWithRetry(IPEndPoint source, bool isTcp)
    {
        const int MaxAttempts = 5;
        const int DelayMs = 10;

        for (int i = 0; i < MaxAttempts; i++)
        {
            int? pid = isTcp
                ? _processResolver.GetTcpPid(source)
                : _processResolver.GetUdpPid(source);

            if (pid is not null)
            {
                string? path = isTcp
                    ? _processResolver.ResolveTcpProcess(source)
                    : _processResolver.ResolveUdpProcess(source);

                if (path is not null)
                    return (pid, path);
            }

            if (i < MaxAttempts - 1)
                Thread.Sleep(DelayMs);
        }

        return (null, null);
    }

    private void HandleFlowEnd(PacketInfo packet)
    {
        if (_registry.TryGet(packet.FlowId, out var entry) && entry is not null)
        {
            if (_driver is WinpkFilterPacketDriver wpf)
                wpf.RemoveNatEntry(entry.OriginalSource);

            _registry.Remove(packet.FlowId);
            SessionClosed?.Invoke(this, entry);
        }
    }
}
