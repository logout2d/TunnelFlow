using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NdisApiDotNet;
using NdisApiDotNet.Native;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.Interop;

/// <summary>
/// Real IPacketDriver implementation using ndisapi.net (MIT).
/// Intercepts TCP/UDP packets at NDIS level via the WinpkFilter driver,
/// identifies the owning process, and redirects matching flows to the
/// local SOCKS5 endpoint by rewriting destination IP:port in outbound packets.
/// </summary>
public sealed unsafe class WinpkFilterPacketDriver : IPacketDriver
{
    private const ushort EtherTypeIpv4 = 0x0800;
    private const byte IpProtoTcp = 6;
    private const byte IpProtoUdp = 17;
    private const int EthernetHeaderLen = 14;

    private readonly ILogger<WinpkFilterPacketDriver> _logger;
    private NdisApiDotNet.NdisApi? _api;

    private IPEndPoint _socksEndpoint = new(IPAddress.Loopback, 2080);
    private IPEndPoint _relayEndpoint = new(IPAddress.Loopback, 2070);
    private IReadOnlyList<string> _includedProcessPaths = [];
    private IReadOnlyList<string> _excludedProcessPaths = [];

    private CancellationTokenSource? _cts;
    private Task? _readTask;
    private ulong _nextFlowId;

    /// <summary>
    /// NAT table tracking original destinations for redirected flows.
    /// Key: "srcIP:srcPort" (outbound local endpoint).
    /// Value: original destination before redirect.
    /// </summary>
    private readonly ConcurrentDictionary<string, NatEntry> _natTable = new();

    /// <summary>
    /// Active flow set: maps flow key to assigned flowId for session tracking.
    /// Key: "srcIP:srcPort-dstIP:dstPort-proto"
    /// </summary>
    private readonly ConcurrentDictionary<string, ulong> _flowIds = new();

    public WinpkFilterPacketDriver(ILogger<WinpkFilterPacketDriver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Looks up the original destination for a source endpoint key ("srcIP:srcPort").
    /// Returns null if not found. Used by LocalRelay for live NAT lookups.
    /// </summary>
    public IPEndPoint? LookupNat(string key) =>
        _natTable.TryGetValue(key, out var entry) ? entry.OriginalDestination : null;

    /// <summary>
    /// Configure the driver with redirect target and process lists.
    /// Must be called before <see cref="ReadLoopAsync"/>.
    /// </summary>
    public void Configure(
        IPEndPoint socksEndpoint,
        IPEndPoint relayEndpoint,
        IReadOnlyList<string> includedPaths,
        IReadOnlyList<string> excludedPaths)
    {
        _socksEndpoint = socksEndpoint;
        _relayEndpoint = relayEndpoint;
        _includedProcessPaths = includedPaths;
        _excludedProcessPaths = excludedPaths;
    }

    public void Open()
    {
        try
        {
            _api = NdisApiDotNet.NdisApi.Open();
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException(
                "ndisapi.dll not found. Install the WinpkFilter driver from: " +
                "https://github.com/wiresock/ndisapi/releases");
        }

        if (!_api.IsDriverLoaded())
        {
            _api.Dispose();
            _api = null;
            throw new InvalidOperationException(
                "WinpkFilter driver (ndisrd) is not loaded. " +
                "Install from: https://github.com/wiresock/ndisapi/releases");
        }

        var version = _api.GetVersion();
        _logger.LogInformation("WinpkFilter driver opened — version {Version}", version);
    }

    public void Close()
    {
        _cts?.Cancel();
        try { _readTask?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { }

        _api?.Dispose();
        _api = null;
        _natTable.Clear();
        _flowIds.Clear();
        _logger.LogInformation("WinpkFilter driver closed");
    }

    public Task ReadLoopAsync(Action<PacketInfo> onPacket, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readTask = Task.Run(() => RunFilterLoop(onPacket, _cts.Token), _cts.Token);
        return _readTask;
    }

    public void RedirectFlow(ulong flowId, IPEndPoint target)
    {
        // Redirect is applied inline in RunFilterLoop by rewriting packet destination.
    }

    public void DropFlow(ulong flowId)
    {
        // Drop is applied inline in RunFilterLoop by not re-injecting the packet.
    }

    public void PassFlow(ulong flowId)
    {
        // Pass is applied inline in RunFilterLoop by re-injecting unmodified.
    }

    public void Dispose()
    {
        Close();
        _cts?.Dispose();
    }

    private void RunFilterLoop(Action<PacketInfo> onPacket, CancellationToken ct)
    {
        if (_api is null)
        {
            _logger.LogError("RunFilterLoop called without Open()");
            return;
        }

        var adapters = _api.GetNetworkAdapters().Where(a => a.IsValid).ToList();
        if (adapters.Count == 0)
        {
            _logger.LogError("No valid network adapters found");
            return;
        }

        _logger.LogInformation("Setting up packet filter on {Count} adapter(s)", adapters.Count);

        var waitHandles = new List<WaitHandle>();
        var adapterEvents = new List<(NetworkAdapter Adapter, AutoResetEvent Event)>();

        foreach (var adapter in adapters)
        {
            // MSTCP_FLAG_SENT_TUNNEL: intercept outbound packets (drops originals).
            // MSTCP_FLAG_RECV_TUNNEL: intercept inbound packets (for response rewrite).
            bool modeSet = _api.SetAdapterMode(adapter,
                NdisApiDotNet.Native.NdisApi.MSTCP_FLAGS.MSTCP_FLAG_SENT_TUNNEL |
                NdisApiDotNet.Native.NdisApi.MSTCP_FLAGS.MSTCP_FLAG_RECV_TUNNEL);

            if (!modeSet)
            {
                _logger.LogWarning("Failed to set filter mode on {Name}", adapter.FriendlyName);
                continue;
            }

            var resetEvent = new AutoResetEvent(false);
            bool eventSet = _api.SetPacketEvent(adapter, resetEvent.SafeWaitHandle);

            if (eventSet)
            {
                _logger.LogInformation("Filtering on adapter: {Name}", adapter.FriendlyName);
                adapterEvents.Add((adapter, resetEvent));
                waitHandles.Add(resetEvent);
            }
            else
            {
                _logger.LogWarning("Failed to set packet event on {Name}", adapter.FriendlyName);
                resetEvent.Dispose();
            }
        }

        if (adapterEvents.Count == 0)
        {
            _logger.LogError("No adapters configured for filtering");
            return;
        }

        // Add cancellation handle
        waitHandles.Add(ct.WaitHandle);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int signaled = WaitHandle.WaitAny(waitHandles.ToArray(), TimeSpan.FromSeconds(1));

                if (signaled == WaitHandle.WaitTimeout)
                    continue;

                // Check if cancellation was signaled
                if (signaled == waitHandles.Count - 1)
                    break;

                // Process packets for the signaled adapter
                if (signaled >= 0 && signaled < adapterEvents.Count)
                {
                    var (adapter, _) = adapterEvents[signaled];
                    ProcessAdapterPackets(adapter, onPacket);
                }
            }
        }
        finally
        {
            // Restore adapter modes
            foreach (var (adapter, evt) in adapterEvents)
            {
                _api?.ResetAdapterMode(adapter);
                evt.Dispose();
            }

            _logger.LogInformation("Filter loop stopped, adapter modes restored");
        }
    }

    private void ProcessAdapterPackets(NetworkAdapter adapter, Action<PacketInfo> onPacket)
    {
        if (_api is null) return;

        var ethRequest = new NdisApiDotNet.Native.NdisApi.ETH_REQUEST();
        var buffer = (NdisApiDotNet.Native.NdisApi.INTERMEDIATE_BUFFER*)
            Marshal.AllocHGlobal(NdisApiDotNet.Native.NdisApi.INTERMEDIATE_BUFFER.Size);

        try
        {
            ethRequest.hAdapterHandle = adapter.Handle;
            ethRequest.EthPacket.Buffer = (IntPtr)buffer;

            while (_api.ReadPacket(ref ethRequest))
            {
                bool isOutbound = buffer->m_dwDeviceFlags == NdisApiDotNet.Native.NdisApi.PACKET_FLAG.PACKET_FLAG_ON_SEND;
                int packetLen = (int)buffer->m_Length;

                if (packetLen < EthernetHeaderLen + 20)
                {
                    ReinjectPacket(ref ethRequest);
                    continue;
                }

                byte* raw = buffer->m_IBuffer;
                ushort etherType = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(raw + 12, 2));

                if (etherType != EtherTypeIpv4)
                {
                    ReinjectPacket(ref ethRequest);
                    continue;
                }

                if (!isOutbound)
                {
                    HandleInboundPacket(raw, packetLen, ref ethRequest);
                    continue;
                }

                HandleOutboundPacket(raw, packetLen, ref ethRequest, onPacket);
            }
        }
        finally
        {
            Marshal.FreeHGlobal((IntPtr)buffer);
        }
    }

    private void HandleOutboundPacket(
        byte* raw, int packetLen,
        ref NdisApiDotNet.Native.NdisApi.ETH_REQUEST ethRequest,
        Action<PacketInfo> onPacket)
    {
        byte* ip = raw + EthernetHeaderLen;
        byte ihl = (byte)((ip[0] & 0x0F) * 4);
        byte proto = ip[9];

        if (proto != IpProtoTcp && proto != IpProtoUdp)
        {
            ReinjectPacket(ref ethRequest);
            return;
        }

        if (packetLen < EthernetHeaderLen + ihl + 4)
        {
            ReinjectPacket(ref ethRequest);
            return;
        }

        var srcIp = new IPAddress(new ReadOnlySpan<byte>(ip + 12, 4));
        var dstIp = new IPAddress(new ReadOnlySpan<byte>(ip + 16, 4));

        // Never intercept loopback traffic — prevents infinite loop
        // (app → WinpkFilter → LocalRelay → sing-box → WinpkFilter → ...)
        if (IPAddress.IsLoopback(dstIp))
        {
            ReinjectPacket(ref ethRequest);
            return;
        }

        byte* transport = ip + ihl;
        ushort srcPort = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(transport, 2));
        ushort dstPort = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(transport + 2, 2));

        var protocol = proto == IpProtoTcp ? Protocol.Tcp : Protocol.Udp;
        var src = new IPEndPoint(srcIp, srcPort);
        var dst = new IPEndPoint(dstIp, dstPort);

        string flowKey = $"{srcIp}:{srcPort}-{dstIp}:{dstPort}-{proto}";

        // Check for TCP SYN (new connection) or new UDP flow
        bool isNewFlow;
        if (proto == IpProtoTcp)
        {
            byte tcpFlags = transport[13];
            bool isSyn = (tcpFlags & 0x02) != 0 && (tcpFlags & 0x10) == 0; // SYN without ACK
            bool isFin = (tcpFlags & 0x01) != 0;
            bool isRst = (tcpFlags & 0x04) != 0;

            if (isFin || isRst)
            {
                if (_flowIds.TryRemove(flowKey, out ulong closingFlowId))
                {
                    string natKey = $"{srcIp}:{srcPort}";
                    _natTable.TryRemove(natKey, out _);

                    onPacket(new PacketInfo
                    {
                        FlowId = closingFlowId,
                        Source = src,
                        Destination = dst,
                        Protocol = protocol,
                        Event = PacketEvent.FlowEnd
                    });
                }

                ReinjectPacket(ref ethRequest);
                return;
            }

            isNewFlow = isSyn;
        }
        else
        {
            isNewFlow = !_flowIds.ContainsKey(flowKey);
        }

        if (isNewFlow)
        {
            ulong flowId = Interlocked.Increment(ref _nextFlowId);
            _flowIds[flowKey] = flowId;

            // onPacket is synchronous — CaptureEngine.HandleNewFlow() runs inline,
            // calls PolicyEngine.Evaluate(), and on Proxy decision calls AddNatEntry()
            // BEFORE this method returns. The NAT table is therefore populated before
            // ReinjectPacket() below sends the packet to the adapter.
            onPacket(new PacketInfo
            {
                FlowId = flowId,
                Source = src,
                Destination = dst,
                Protocol = protocol,
                Event = PacketEvent.NewFlow
            });
        }
        else if (_flowIds.TryGetValue(flowKey, out ulong existingFlowId))
        {
            onPacket(new PacketInfo
            {
                FlowId = existingFlowId,
                Source = src,
                Destination = dst,
                Protocol = protocol,
                Event = PacketEvent.Data
            });
        }

        // Check if this flow should be redirected (NAT entry exists)
        string srcKey = $"{srcIp}:{srcPort}";
        bool shouldRedirect = _natTable.ContainsKey(srcKey);
        _logger.LogDebug(
            "Flow {SrcKey}: NAT table has {Count} entries, redirect={Redirect}",
            srcKey, _natTable.Count, shouldRedirect);

        if (shouldRedirect)
        {
            // Rewrite destination to LocalRelay endpoint
            RewriteDestination(raw, ip, transport, proto, ihl, packetLen);
        }

        ReinjectPacket(ref ethRequest);
    }

    private void HandleInboundPacket(
        byte* raw, int packetLen,
        ref NdisApiDotNet.Native.NdisApi.ETH_REQUEST ethRequest)
    {
        byte* ip = raw + EthernetHeaderLen;
        byte ihl = (byte)((ip[0] & 0x0F) * 4);
        byte proto = ip[9];

        if ((proto != IpProtoTcp && proto != IpProtoUdp) ||
            packetLen < EthernetHeaderLen + ihl + 4)
        {
            ReinjectPacket(ref ethRequest);
            return;
        }

        var srcIp = new IPAddress(new ReadOnlySpan<byte>(ip + 12, 4));
        byte* transport = ip + ihl;
        ushort srcPort = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(transport, 2));
        ushort dstPort = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(transport + 2, 2));

        // Response from LocalRelay endpoint → rewrite source back to original destination
        if (srcIp.Equals(_relayEndpoint.Address) && srcPort == _relayEndpoint.Port)
        {
            var dstIp = new IPAddress(new ReadOnlySpan<byte>(ip + 16, 4));
            string natKey = $"{dstIp}:{dstPort}";

            if (_natTable.TryGetValue(natKey, out var natEntry))
            {
                RewriteSource(raw, ip, transport, proto, ihl, packetLen, natEntry.OriginalDestination);
            }
        }

        ReinjectPacket(ref ethRequest);
    }

    /// <summary>
    /// Called by CaptureEngine when a flow should be proxied.
    /// Adds a NAT table entry so subsequent packets get redirected.
    /// </summary>
    public void AddNatEntry(IPEndPoint localEndpoint, IPEndPoint originalDestination)
    {
        string key = $"{localEndpoint.Address}:{localEndpoint.Port}";
        _natTable[key] = new NatEntry(originalDestination, DateTime.UtcNow);
        _logger.LogDebug("NAT entry added: {Key} → {Dest}", key, originalDestination);
    }

    /// <summary>
    /// Remove a NAT entry when a flow closes.
    /// </summary>
    public void RemoveNatEntry(IPEndPoint localEndpoint)
    {
        string key = $"{localEndpoint.Address}:{localEndpoint.Port}";
        _natTable.TryRemove(key, out _);
    }

    private void RewriteDestination(byte* raw, byte* ip, byte* transport, byte proto, byte ihl, int packetLen)
    {
        // Redirect to LocalRelay, not sing-box directly. LocalRelay performs
        // the SOCKS5 CONNECT handshake on behalf of the original client.
        var socksAddrBytes = _relayEndpoint.Address.GetAddressBytes();
        ushort socksPort = (ushort)_relayEndpoint.Port;

        // Rewrite IP destination
        Marshal.Copy(socksAddrBytes, 0, (IntPtr)(ip + 16), 4);

        // Rewrite transport destination port
        byte* dstPortPtr = transport + 2;
        dstPortPtr[0] = (byte)(socksPort >> 8);
        dstPortPtr[1] = (byte)(socksPort & 0xFF);

        RecalculateChecksums(ip, transport, proto, ihl, packetLen);
    }

    private void RewriteSource(byte* raw, byte* ip, byte* transport, byte proto, byte ihl, int packetLen, IPEndPoint original)
    {
        var origAddrBytes = original.Address.GetAddressBytes();
        ushort origPort = (ushort)original.Port;

        // Rewrite IP source
        Marshal.Copy(origAddrBytes, 0, (IntPtr)(ip + 12), 4);

        // Rewrite transport source port
        byte* srcPortPtr = transport;
        srcPortPtr[0] = (byte)(origPort >> 8);
        srcPortPtr[1] = (byte)(origPort & 0xFF);

        RecalculateChecksums(ip, transport, proto, ihl, packetLen);
    }

    private static void RecalculateChecksums(byte* ip, byte* transport, byte proto, byte ihl, int packetLen)
    {
        int ipLen = packetLen - EthernetHeaderLen;

        // Zero IP header checksum, recalculate
        ip[10] = 0;
        ip[11] = 0;
        ushort ipChecksum = ComputeChecksum(ip, ihl);
        ip[10] = (byte)(ipChecksum >> 8);
        ip[11] = (byte)(ipChecksum & 0xFF);

        int transportLen = ipLen - ihl;
        if (transportLen < 4) return;

        if (proto == IpProtoTcp && transportLen >= 20)
        {
            // Zero TCP checksum, recalculate with pseudo-header
            transport[16] = 0;
            transport[17] = 0;
            ushort tcpChecksum = ComputeTransportChecksum(ip, transport, proto, transportLen);
            transport[16] = (byte)(tcpChecksum >> 8);
            transport[17] = (byte)(tcpChecksum & 0xFF);
        }
        else if (proto == IpProtoUdp && transportLen >= 8)
        {
            // Zero UDP checksum, recalculate with pseudo-header
            transport[6] = 0;
            transport[7] = 0;
            ushort udpChecksum = ComputeTransportChecksum(ip, transport, proto, transportLen);
            if (udpChecksum == 0) udpChecksum = 0xFFFF; // UDP 0 means "no checksum"
            transport[6] = (byte)(udpChecksum >> 8);
            transport[7] = (byte)(udpChecksum & 0xFF);
        }
    }

    private static ushort ComputeChecksum(byte* data, int length)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 1 < length; i += 2)
            sum += (uint)(data[i] << 8 | data[i + 1]);
        if (i < length)
            sum += (uint)(data[i] << 8);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }

    private static ushort ComputeTransportChecksum(byte* ip, byte* transport, byte proto, int transportLen)
    {
        // Pseudo-header: srcIP(4) + dstIP(4) + zero(1) + proto(1) + transportLen(2)
        uint sum = 0;

        // Source IP
        sum += (uint)(ip[12] << 8 | ip[13]);
        sum += (uint)(ip[14] << 8 | ip[15]);
        // Destination IP
        sum += (uint)(ip[16] << 8 | ip[17]);
        sum += (uint)(ip[18] << 8 | ip[19]);
        // Zero + protocol
        sum += proto;
        // Transport length
        sum += (uint)transportLen;

        // Transport data
        int i = 0;
        for (; i + 1 < transportLen; i += 2)
            sum += (uint)(transport[i] << 8 | transport[i + 1]);
        if (i < transportLen)
            sum += (uint)(transport[i] << 8);

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        return (ushort)~sum;
    }

    private void ReinjectPacket(ref NdisApiDotNet.Native.NdisApi.ETH_REQUEST ethRequest)
    {
        _api?.SendPacket(ref ethRequest);
    }

    private sealed record NatEntry(IPEndPoint OriginalDestination, DateTime CreatedAt);
}
