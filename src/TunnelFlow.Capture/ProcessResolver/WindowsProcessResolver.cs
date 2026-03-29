using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace TunnelFlow.Capture.ProcessResolver;

public sealed class WindowsProcessResolver : IProcessResolver
{
    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint NO_ERROR = 0;

    private readonly ConcurrentDictionary<int, (string path, DateTime expiry)> _pidPathCache = new();

    public string? ResolveTcpProcess(IPEndPoint localEndpoint)
    {
        int? pid = FindPidInTcpTable(localEndpoint);
        return pid.HasValue ? GetProcessPath(pid.Value) : null;
    }

    public string? ResolveUdpProcess(IPEndPoint localEndpoint)
    {
        int? pid = FindPidInUdpTable(localEndpoint);
        return pid.HasValue ? GetProcessPath(pid.Value) : null;
    }

    public int? GetTcpPid(IPEndPoint localEndpoint) => FindPidInTcpTable(localEndpoint);

    public int? GetUdpPid(IPEndPoint localEndpoint) => FindPidInUdpTable(localEndpoint);

    private int? FindPidInTcpTable(IPEndPoint localEndpoint)
    {
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint result = GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != NO_ERROR) return null;

            int numEntries = Marshal.ReadInt32(buffer);
            nint rowPtr = buffer + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            ushort targetPort = (ushort)localEndpoint.Port;
            uint targetAddr = BitConverter.ToUInt32(localEndpoint.Address.GetAddressBytes(), 0);

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                ushort rowPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);

                if (rowPort == targetPort && row.dwLocalAddr == targetAddr)
                {
                    return (int)row.dwOwningPid;
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private int? FindPidInUdpTable(IPEndPoint localEndpoint)
    {
        int size = 0;
        GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            uint result = GetExtendedUdpTable(buffer, ref size, false, AF_INET, UDP_TABLE_OWNER_PID, 0);
            if (result != NO_ERROR) return null;

            int numEntries = Marshal.ReadInt32(buffer);
            nint rowPtr = buffer + 4;
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            ushort targetPort = (ushort)localEndpoint.Port;
            uint targetAddr = BitConverter.ToUInt32(localEndpoint.Address.GetAddressBytes(), 0);

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                ushort rowPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);

                if (rowPort == targetPort && row.dwLocalAddr == targetAddr)
                {
                    return (int)row.dwOwningPid;
                }

                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return null;
    }

    private string? GetProcessPath(int pid)
    {
        if (_pidPathCache.TryGetValue(pid, out var cached) && cached.expiry > DateTime.UtcNow)
        {
            return cached.path;
        }

        nint hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return null;

        try
        {
            var sb = new StringBuilder(1024);
            uint capacity = (uint)sb.Capacity;

            if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
            {
                string path = sb.ToString();
                _pidPathCache[pid] = (path, DateTime.UtcNow.AddSeconds(5));
                return path;
            }
        }
        finally
        {
            CloseHandle(hProcess);
        }

        return null;
    }

    #region P/Invoke

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int TableClass, int Reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, int TableClass, int Reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        nint hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwOwningPid;
    }

    #endregion
}
