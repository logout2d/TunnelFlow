using System.Diagnostics;
using System.Globalization;
using System.Net;
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

        return Task.FromResult(WfpNativeSessionHandle.CreateNative(
            process,
            process.StandardInput,
            process.StandardOutput));
    }

    public async Task CloseSessionAsync(
        WfpNativeSessionHandle handle,
        CancellationToken ct = default)
    {
        if (handle.Mode != WfpNativeSessionMode.Native || handle.Process is null)
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
        if (handle.Mode != WfpNativeSessionMode.Native || handle.Reader is null)
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
        if (handle.Mode != WfpNativeSessionMode.Native || handle.Writer is null)
            return;

        string line = BuildEmitLine(redirectEvent);
        await handle.Writer.WriteLineAsync(line);
        await handle.Writer.FlushAsync();
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
}

public sealed class WfpNativeSessionHandle
{
    private WfpNativeSessionHandle(
        WfpNativeSessionMode mode,
        Guid id,
        Process? process,
        StreamWriter? writer,
        StreamReader? reader)
    {
        Mode = mode;
        Id = id;
        Process = process;
        Writer = writer;
        Reader = reader;
    }

    public WfpNativeSessionMode Mode { get; }

    public Guid Id { get; }

    public Process? Process { get; }

    public StreamWriter? Writer { get; }

    public StreamReader? Reader { get; }

    public static WfpNativeSessionHandle CreateStub() =>
        new(WfpNativeSessionMode.Stub, Guid.NewGuid(), null, null, null);

    public static WfpNativeSessionHandle CreateNative(
        Process process,
        StreamWriter writer,
        StreamReader reader) =>
        new(WfpNativeSessionMode.Native, Guid.NewGuid(), process, writer, reader);
}

public enum WfpNativeSessionMode
{
    Stub = 0,
    Native = 1
}
