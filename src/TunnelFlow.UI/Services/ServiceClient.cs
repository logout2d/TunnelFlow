using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TunnelFlow.Core.IPC.Messages;

namespace TunnelFlow.UI.Services;

public sealed class ServiceClient : IDisposable
{
    private const string PipeName = "TunnelFlowService";

    private static readonly Encoding _utf8NoBom =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new IPEndPointJsonConverter()
        }
    };

    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();

    private CancellationTokenSource? _connectionCts;
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;

    public event EventHandler<EventMessage>? EventReceived;
    public event EventHandler? Disconnected;
    public event EventHandler? Connected;
    public event EventHandler<string>? DiagnosticMessage;

    public bool IsConnected { get; private set; }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _connectionCts?.Dispose();
        _pipe?.Dispose();
        _lifetimeCts.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>
    /// Connect and start the read loop. Returns only after the connection
    /// is established AND ReadLoopAsync is actively reading.
    /// Retries indefinitely with exponential backoff until connected or ct is cancelled.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        // Do NOT use 'using' — this CTS must stay alive for the ReadLoopAsync lifetime.
        // It is stored in _connectionCts and disposed when ServiceClient is disposed.
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        _connectionCts = linked;
        await ConnectWithRetryAsync(linked.Token);
    }

    public async Task<JsonElement?> SendCommandAsync(string type, object? payload, CancellationToken ct)
    {
        if (!IsConnected || _writer is null)
            throw new InvalidOperationException("Not connected to service");

        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var msg = new JsonObject { ["type"] = type, ["id"] = id };
            if (payload is not null)
                msg["payload"] = JsonSerializer.SerializeToNode(payload, _options);

            var line = msg.ToJsonString(_options) + "\n";

            await _writeLock.WaitAsync(ct);
            try { await _writer.WriteAsync(line.AsMemory(), ct); }
            finally { _writeLock.Release(); }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        int delaySec = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", PipeName,
                    PipeDirection.InOut, PipeOptions.Asynchronous);

                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                await pipe.ConnectAsync(connectTimeout.Token);

                _pipe = pipe;
                _writer = new StreamWriter(pipe, _utf8NoBom) { AutoFlush = true };

                // Start the read loop and wait until it signals that it is actively
                // reading — only then is it safe to send commands and fire Connected.
                var readLoopReady = new TaskCompletionSource();
                _ = ReadLoopAsync(pipe, readLoopReady, ct);
                await readLoopReady.Task;

                IsConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _pipe?.Dispose();
                _pipe = null;
                _writer = null;
                DiagnosticMessage?.Invoke(this,
                    $"Connect failed (retry in {delaySec}s): {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
                delaySec = Math.Min(delaySec * 2, 30);
            }
        }
    }

    private async Task ReadLoopAsync(
        NamedPipeClientStream pipe,
        TaskCompletionSource readLoopReady,
        CancellationToken ct)
    {
        var reader = new StreamReader(pipe, _utf8NoBom);

        // Signal before entering the loop: the reader is positioned and waiting.
        // ConnectWithRetryAsync awaits this before firing Connected, guaranteeing
        // that any SendCommandAsync call triggered by Connected will have its
        // response picked up by this reader.
        readLoopReady.TrySetResult();

        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DiagnosticMessage?.Invoke(this, $"ReadLoop error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            IsConnected = false;

            foreach (var (_, tcs) in _pending)
                tcs.TrySetCanceled();
            _pending.Clear();

            Disconnected?.Invoke(this, EventArgs.Empty);

            if (!ct.IsCancellationRequested)
                _ = ConnectWithRetryAsync(ct);
        }
    }

    private void ProcessLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            if (type is "Ok" or "Error")
            {
                if (!root.TryGetProperty("id", out var idEl)) return;
                var id = idEl.GetString();
                if (id is null || !_pending.TryRemove(id, out var tcs)) return;

                if (type == "Error")
                {
                    var msg = "Service error";
                    if (root.TryGetProperty("payload", out var ep) &&
                        ep.TryGetProperty("message", out var mel))
                        msg = mel.GetString() ?? msg;
                    tcs.TrySetException(new InvalidOperationException(msg));
                }
                else
                {
                    JsonElement? payload = null;
                    if (root.TryGetProperty("payload", out var p) &&
                        p.ValueKind != JsonValueKind.Null)
                        payload = p.Clone();
                    tcs.TrySetResult(payload);
                }
            }
            else
            {
                JsonElement? payload = null;
                if (root.TryGetProperty("payload", out var payloadEl) &&
                    payloadEl.ValueKind != JsonValueKind.Null)
                    payload = payloadEl.Clone();

                EventReceived?.Invoke(this, new EventMessage { Type = type!, Payload = payload });
            }
        }
        catch { }
    }

    private sealed class IPEndPointJsonConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint? Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions o)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("address", out var a) || !root.TryGetProperty("port", out var p))
                return new IPEndPoint(IPAddress.Any, 0);
            return new IPEndPoint(IPAddress.Parse(a.GetString() ?? "0.0.0.0"), p.GetInt32());
        }

        public override void Write(Utf8JsonWriter w, IPEndPoint v, JsonSerializerOptions o)
        {
            w.WriteStartObject();
            w.WriteString("address", v.Address.ToString());
            w.WriteNumber("port", v.Port);
            w.WriteEndObject();
        }
    }
}
