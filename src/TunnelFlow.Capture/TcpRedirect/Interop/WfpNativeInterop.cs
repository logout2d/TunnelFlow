using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;

namespace TunnelFlow.Capture.TcpRedirect.Interop;

public sealed class WfpNativeInterop
{
    private readonly string? _helperPath;

    public WfpNativeInterop(string? helperPath = null)
    {
        _helperPath = helperPath;
    }

    public Task<WfpNativeSessionHandle> OpenSessionAsync(
        WfpRedirectConfig config,
        CancellationToken ct = default)
    {
        string devicePath = ResolveDevicePath(config);
        SafeFileHandle? deviceHandle = TryOpenDevice(devicePath);
        if (deviceHandle is not null)
        {
            SendConfigureRequest(deviceHandle, config);
            return Task.FromResult(WfpNativeSessionHandle.CreateDevice(deviceHandle, devicePath));
        }

        string? helperPath = ResolveHelperPath();
        if (string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
            return Task.FromResult(WfpNativeSessionHandle.CreateStub());

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start native WFP helper: {helperPath}");

        return Task.FromResult(WfpNativeSessionHandle.CreateHelper(
            process,
            process.StandardInput,
            process.StandardOutput));
    }

    public async Task CloseSessionAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct = default)
    {
        if (handle.Mode == WfpNativeSessionMode.Device)
        {
            handle.DeviceHandle?.Dispose();
            return;
        }

        if (handle.Mode != WfpNativeSessionMode.Helper || handle.Process is null)
            return;

        if (!handle.Process.HasExited && handle.Writer is not null)
        {
            await handle.Writer.WriteLineAsync("STOP");
            await handle.Writer.FlushAsync();
        }

        try
        {
            await handle.Process.WaitForExitAsync(ct);
        }
        catch
        {
            if (!handle.Process.HasExited)
            {
                handle.Process.Kill(entireProcessTree: true);
                await handle.Process.WaitForExitAsync(ct);
            }
        }

        handle.Process.Dispose();
    }

    public async Task<WfpRedirectEvent?> TryReadRedirectEventAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct = default)
    {
        if (handle.Mode == WfpNativeSessionMode.Device)
            return await TryReadRedirectEventFromDeviceAsync(handle, ct);

        if (handle.Mode != WfpNativeSessionMode.Helper || handle.Reader is null)
            return null;

        while (!ct.IsCancellationRequested)
        {
            string? line = await handle.Reader.ReadLineAsync(ct);
            if (line is null)
                return null;

            if (TryParseEvent(line, out var redirectEvent))
                return redirectEvent;
        }

        return null;
    }

    internal async Task SendSyntheticRedirectEventAsync(
        WfpNativeSessionHandle handle,
        WfpRedirectEvent redirectEvent,
        CancellationToken ct = default)
    {
        if (handle.Mode != WfpNativeSessionMode.Helper || handle.Writer is null)
            return;

        string line = BuildEmitLine(redirectEvent);
        await handle.Writer.WriteLineAsync(line);
        await handle.Writer.FlushAsync();
    }

    private async Task<WfpRedirectEvent?> TryReadRedirectEventFromDeviceAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct)
    {
        if (handle.DeviceHandle is null || handle.DeviceHandle.IsInvalid)
            return null;

        return await Task.Run(() =>
        {
            byte[] outputBuffer = new byte[Marshal.SizeOf<WfpNativeRedirectEventV1>()];
            bool success = NativeMethods.DeviceIoControl(
                handle.DeviceHandle,
                WfpNativeContract.IoctlGetNextEvent,
                null,
                0,
                outputBuffer,
                outputBuffer.Length,
                out uint bytesReturned,
                IntPtr.Zero);

            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error is NativeMethods.ErrorFileNotFound or NativeMethods.ErrorPathNotFound or NativeMethods.ErrorNoMoreItems)
                    return null;

                throw new Win32Exception(error, $"WFP device read failed for {handle.DevicePath ?? WfpNativeContract.DefaultDevicePath}");
            }

            if (bytesReturned == 0)
                return null;

            return WfpNativeContract.TryParseRedirectEvent(
                outputBuffer.AsSpan(0, checked((int)bytesReturned)),
                out var redirectEvent)
                ? redirectEvent
                : throw new InvalidDataException("Native WFP device returned an unreadable redirect event payload.");
        }, ct);
    }

    private string? ResolveHelperPath()
    {
        if (!string.IsNullOrWhiteSpace(_helperPath))
            return _helperPath;

        string? envPath = Environment.GetEnvironmentVariable("TUNNELFLOW_WFP_NATIVE_HELPER");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "native",
                "TunnelFlow.WfpRedirectChannel",
                "x64",
                "Debug",
                "TunnelFlow.WfpRedirectChannel.exe"));
    }

    private string ResolveDevicePath(WfpRedirectConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.NativeDevicePath))
            return config.NativeDevicePath;

        string? envPath = Environment.GetEnvironmentVariable("TUNNELFLOW_WFP_NATIVE_DEVICE");
        if (!string.IsNullOrWhiteSpace(envPath))
            return envPath;

        return WfpNativeContract.DefaultDevicePath;
    }

    private static SafeFileHandle? TryOpenDevice(string devicePath)
    {
        SafeFileHandle handle = NativeMethods.CreateFile(
            devicePath,
            NativeMethods.GenericRead | NativeMethods.GenericWrite,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileAttributeNormal,
            IntPtr.Zero);

        if (!handle.IsInvalid)
            return handle;

        int error = Marshal.GetLastWin32Error();
        handle.Dispose();

        if (error is NativeMethods.ErrorFileNotFound or NativeMethods.ErrorPathNotFound)
            return null;

        throw new Win32Exception(error, $"Failed to open WFP redirect device: {devicePath}");
    }

    private static void SendConfigureRequest(SafeFileHandle deviceHandle, WfpRedirectConfig config)
    {
        byte[] inputBuffer = WfpNativeContract.BuildConfigureRequest(config);
        bool success = NativeMethods.DeviceIoControl(
            deviceHandle,
            WfpNativeContract.IoctlConfigure,
            inputBuffer,
            inputBuffer.Length,
            null,
            0,
            out _,
            IntPtr.Zero);

        if (!success)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "Failed to configure the WFP redirect device.");
        }
    }

    private static string BuildEmitLine(WfpRedirectEvent redirectEvent)
    {
        var parts = new[]
        {
            "EMIT",
            redirectEvent.LookupKey.ClientAddress.ToString(),
            redirectEvent.LookupKey.ClientPort.ToString(CultureInfo.InvariantCulture),
            redirectEvent.OriginalDestination.Address.ToString(),
            redirectEvent.OriginalDestination.Port.ToString(CultureInfo.InvariantCulture),
            redirectEvent.RelayEndpoint.Address.ToString(),
            redirectEvent.RelayEndpoint.Port.ToString(CultureInfo.InvariantCulture),
            (redirectEvent.ProcessId ?? -1).ToString(CultureInfo.InvariantCulture),
            EncodeField(redirectEvent.ProcessPath),
            EncodeField(redirectEvent.AppId),
            redirectEvent.Protocol.ToString(),
            redirectEvent.CorrelationId.ToString(),
            redirectEvent.ObservedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)
        };

        return string.Join('|', parts);
    }

    private static bool TryParseEvent(string line, out WfpRedirectEvent redirectEvent)
    {
        redirectEvent = null!;
        if (!line.StartsWith("EVENT|", StringComparison.Ordinal))
            return false;

        string[] parts = line.Split('|');
        if (parts.Length != 13)
            return false;

        if (!IPAddress.TryParse(parts[1], out var lookupAddress) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lookupPort) ||
            !IPAddress.TryParse(parts[3], out var originalAddress) ||
            !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var originalPort) ||
            !IPAddress.TryParse(parts[5], out var relayAddress) ||
            !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var relayPort) ||
            !int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var processIdValue) ||
            !Enum.TryParse<TunnelFlow.Core.Models.Protocol>(parts[10], ignoreCase: true, out var protocol) ||
            !Guid.TryParse(parts[11], out var correlationId) ||
            !long.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var observedTicks))
        {
            return false;
        }

        redirectEvent = new WfpRedirectEvent
        {
            LookupKey = new ConnectionLookupKey(lookupAddress, lookupPort),
            OriginalDestination = new IPEndPoint(originalAddress, originalPort),
            RelayEndpoint = new IPEndPoint(relayAddress, relayPort),
            ProcessId = processIdValue >= 0 ? processIdValue : null,
            ProcessPath = DecodeField(parts[8]),
            AppId = DecodeField(parts[9]),
            Protocol = protocol,
            CorrelationId = correlationId,
            ObservedAtUtc = new DateTime(observedTicks, DateTimeKind.Utc)
        };
        return true;
    }

    private static string EncodeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "-";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    private static string? DecodeField(string value)
    {
        if (value == "-")
            return null;

        return Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;
        internal const int ErrorFileNotFound = 2;
        internal const int ErrorPathNotFound = 3;
        internal const int ErrorNoMoreItems = 259;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            byte[]? lpInBuffer,
            int nInBufferSize,
            byte[]? lpOutBuffer,
            int nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}

public sealed class WfpNativeSessionHandle
{
    private WfpNativeSessionHandle(
        WfpNativeSessionMode mode,
        Guid id,
        SafeFileHandle? deviceHandle,
        string? devicePath,
        Process? process,
        StreamWriter? writer,
        StreamReader? reader)
    {
        Mode = mode;
        Id = id;
        DeviceHandle = deviceHandle;
        DevicePath = devicePath;
        Process = process;
        Writer = writer;
        Reader = reader;
    }

    public WfpNativeSessionMode Mode { get; }

    public Guid Id { get; }

    public SafeFileHandle? DeviceHandle { get; }

    public string? DevicePath { get; }

    public Process? Process { get; }

    public StreamWriter? Writer { get; }

    public StreamReader? Reader { get; }

    public static WfpNativeSessionHandle CreateStub() =>
        new(WfpNativeSessionMode.Stub, Guid.NewGuid(), null, null, null, null, null);

    public static WfpNativeSessionHandle CreateDevice(
        SafeFileHandle deviceHandle,
        string devicePath) =>
        new(WfpNativeSessionMode.Device, Guid.NewGuid(), deviceHandle, devicePath, null, null, null);

    public static WfpNativeSessionHandle CreateHelper(
        Process process,
        StreamWriter writer,
        StreamReader reader) =>
        new(WfpNativeSessionMode.Helper, Guid.NewGuid(), null, null, process, writer, reader);
}

public enum WfpNativeSessionMode
{
    Stub = 0,
    Helper = 1,
    Device = 2
}
