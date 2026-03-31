using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Capture.TcpRedirect.Interop;

internal static class WfpNativeContract
{
    internal const string DefaultDevicePath = @"\\.\TunnelFlowWfpRedirect";
    internal const uint ContractVersion = 1;
    internal const int MaxPathChars = 260;
    internal const int FileDeviceTunnelFlowRedirect = unchecked((int)0x8000);
    internal const uint IoctlConfigure = 0x80002000;
    internal const uint IoctlGetNextEvent = 0x80002004;
    internal const uint ConfigureFlagEnableDetailedLogging = 0x00000001;

    internal static unsafe byte[] BuildConfigureRequest(WfpRedirectConfig config)
    {
        WfpNativeConfigureRequestV1 request = default;
        request.Version = ContractVersion;
        request.Size = (uint)sizeof(WfpNativeConfigureRequestV1);
        request.Flags = config.EnableDetailedLogging ? ConfigureFlagEnableDetailedLogging : 0;

        if (config.RelayEndpoint is not null && config.RelayEndpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            request.RelayAddressV4 = ToIpv4NetworkOrder(config.RelayEndpoint.Address);
            request.RelayPort = checked((ushort)config.RelayEndpoint.Port);
        }

        WriteFixedString(request.TestProcessPath, MaxPathChars, config.TestProcessPath);

        return StructureToBytes(request);
    }

    internal static unsafe byte[] BuildRedirectEventPayload(WfpRedirectEvent redirectEvent)
    {
        WfpNativeRedirectEventV1 payload = default;
        payload.Version = ContractVersion;
        payload.Size = (uint)sizeof(WfpNativeRedirectEventV1);
        payload.LookupAddressV4 = ToIpv4NetworkOrder(redirectEvent.LookupKey.ClientAddress);
        payload.OriginalAddressV4 = ToIpv4NetworkOrder(redirectEvent.OriginalDestination.Address);
        payload.RelayAddressV4 = ToIpv4NetworkOrder(redirectEvent.RelayEndpoint.Address);
        payload.LookupPort = checked((ushort)redirectEvent.LookupKey.ClientPort);
        payload.OriginalPort = checked((ushort)redirectEvent.OriginalDestination.Port);
        payload.RelayPort = checked((ushort)redirectEvent.RelayEndpoint.Port);
        payload.ProcessId = checked((uint)Math.Max(0, redirectEvent.ProcessId ?? 0));
        payload.Protocol = (uint)redirectEvent.Protocol;
        payload.ObservedAtUtcTicks = redirectEvent.ObservedAtUtc.Ticks;
        payload.CorrelationId = redirectEvent.CorrelationId;

        WriteFixedString(payload.ProcessPath, MaxPathChars, redirectEvent.ProcessPath);
        WriteFixedString(payload.AppId, MaxPathChars, redirectEvent.AppId);

        return StructureToBytes(payload);
    }

    internal static unsafe bool TryParseRedirectEvent(
        ReadOnlySpan<byte> buffer,
        out WfpRedirectEvent redirectEvent)
    {
        redirectEvent = null!;

        if (buffer.Length < sizeof(WfpNativeRedirectEventV1))
            return false;

        WfpNativeRedirectEventV1 payload = BytesToStructure<WfpNativeRedirectEventV1>(buffer);
        if (payload.Version != ContractVersion || payload.Size < sizeof(WfpNativeRedirectEventV1))
            return false;

        int protocolValue = unchecked((int)payload.Protocol);
        var protocol =
            protocolValue == (int)Protocol.Tcp || protocolValue == (int)Protocol.Udp
                ? (Protocol)protocolValue
                : Protocol.Tcp;

        string? processPath;
        string? appId;

        processPath = ReadFixedString(payload.ProcessPath, MaxPathChars);
        appId = ReadFixedString(payload.AppId, MaxPathChars);

        redirectEvent = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(
                FromIpv4NetworkOrder(payload.LookupAddressV4),
                payload.LookupPort),
            OriginalDestination = new IPEndPoint(
                FromIpv4NetworkOrder(payload.OriginalAddressV4),
                payload.OriginalPort),
            RelayEndpoint = new IPEndPoint(
                FromIpv4NetworkOrder(payload.RelayAddressV4),
                payload.RelayPort),
            ProcessId = payload.ProcessId == 0 ? null : checked((int)payload.ProcessId),
            ProcessPath = processPath,
            AppId = appId,
            Protocol = protocol,
            CorrelationId = payload.CorrelationId == Guid.Empty ? Guid.NewGuid() : payload.CorrelationId,
            ObservedAtUtc = payload.ObservedAtUtcTicks > 0
                ? new DateTime(payload.ObservedAtUtcTicks, DateTimeKind.Utc)
                : DateTime.UtcNow
        };

        return true;
    }

    private static unsafe byte[] StructureToBytes<T>(T value)
        where T : unmanaged
    {
        byte[] buffer = new byte[sizeof(T)];
        fixed (byte* raw = buffer)
        {
            *(T*)raw = value;
        }

        return buffer;
    }

    private static unsafe T BytesToStructure<T>(ReadOnlySpan<byte> bytes)
        where T : unmanaged
    {
        fixed (byte* raw = bytes)
        {
            return *(T*)raw;
        }
    }

    private static uint ToIpv4NetworkOrder(IPAddress address)
    {
        byte[] bytes = address.MapToIPv4().GetAddressBytes();
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static IPAddress FromIpv4NetworkOrder(uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    internal static unsafe string? ReadConfigureRequestProcessPath(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(WfpNativeConfigureRequestV1))
            return null;

        WfpNativeConfigureRequestV1 request = BytesToStructure<WfpNativeConfigureRequestV1>(buffer);
        return ReadFixedString(request.TestProcessPath, MaxPathChars);
    }

    internal static unsafe WfpNativeConfigureRequestInfo? ReadConfigureRequestInfo(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(WfpNativeConfigureRequestV1))
            return null;

        WfpNativeConfigureRequestV1 request = BytesToStructure<WfpNativeConfigureRequestV1>(buffer);
        return new WfpNativeConfigureRequestInfo(
            request.Version,
            request.Size,
            request.Flags,
            request.RelayPort,
            ReadFixedString(request.TestProcessPath, MaxPathChars));
    }

    private static unsafe void WriteFixedString(char* destination, int maxChars, string? value)
    {
        for (int i = 0; i < maxChars; i++)
            destination[i] = '\0';

        if (string.IsNullOrEmpty(value))
            return;

        int count = Math.Min(value.Length, maxChars - 1);
        for (int i = 0; i < count; i++)
            destination[i] = value[i];
    }

    private static unsafe string? ReadFixedString(char* source, int maxChars)
    {
        int length = 0;
        while (length < maxChars && source[length] != '\0')
            length++;

        if (length == 0)
            return null;

        return new string(source, 0, length);
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal unsafe struct WfpNativeConfigureRequestV1
{
    public uint Version;
    public uint Size;
    public uint Flags;
    public uint RelayAddressV4;
    public ushort RelayPort;
    public ushort Reserved;
    public fixed char TestProcessPath[WfpNativeContract.MaxPathChars];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
internal unsafe struct WfpNativeRedirectEventV1
{
    public uint Version;
    public uint Size;
    public uint LookupAddressV4;
    public uint OriginalAddressV4;
    public uint RelayAddressV4;
    public ushort LookupPort;
    public ushort OriginalPort;
    public ushort RelayPort;
    public ushort Reserved;
    public uint ProcessId;
    public uint Protocol;
    public long ObservedAtUtcTicks;
    public Guid CorrelationId;
    public fixed char ProcessPath[WfpNativeContract.MaxPathChars];
    public fixed char AppId[WfpNativeContract.MaxPathChars];
}

internal sealed record WfpNativeConfigureRequestInfo(
    uint Version,
    uint Size,
    uint Flags,
    ushort RelayPort,
    string? TestProcessPath);
