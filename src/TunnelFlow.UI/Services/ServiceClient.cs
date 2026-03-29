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

    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new IPEndPointJsonConverter()
        }
    };

    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement?>> _pending = new();

    public event EventHandler<EventMessage>? EventReceived;
    public event EventHandler? Disconnected;
    public event EventHandler? Connected;

    public bool IsConnected { get; private set; }

    /// <summary>Connect to the service pipe with exponential-backoff retry until cancelled.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetimeCts.Token);
        await ConnectWithRetryAsync(linked.Token);
    }

    /// <summary>Send a command and return the Ok payload, or throw on ErrorResponse.</summary>
    public async Task<JsonElement?> SendCommandAsync(string type, object? payload, CancellationToken ct)
    {
        if (!IsConnected || _writer is null)
            throw new InvalidOperationException("Not connected to service");

        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.TryAdd(id, tcs);

        try
        {
            var msg = new JsonObject { ["type"] = type, ["id"] = id };
            if (payload is not null)
                msg["payload"] = JsonSerializer.SerializeToNode(payload, _options);

            var line = msg.ToJsonString(_options) + "\n";

            await _writeLock.WaitAsync(ct);
            try { await _writer.WriteAsync(line.AsMemory(), ct); }
            finally { _writeLock.Release(); }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            timeout.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();
        _pipe?.Dispose();
        _lifetimeCts.Dispose();
        _writeLock.Dispose();
    }

    private async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        int delaySec = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(TimeSpan.FromSeconds(5));

                await pipe.ConnectAsync(connectCts.Token);

                _pipe = pipe;
                _writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                IsConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);

                _ = ReadLoopAsync(pipe, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Service not available yet — wait and retry
                await Task.Delay(TimeSpan.FromSeconds(delaySec), ct);
                delaySec = Math.Min(delaySec * 2, 30);
            }
        }
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        var reader = new StreamReader(pipe, Encoding.UTF8);
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
        catch { }
        finally
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);

            foreach (var (_, pending) in _pending)
                pending.TrySetCanceled();
            _pending.Clear();

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
                var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (id is null || !_pending.TryRemove(id, out var tcs)) return;

                if (type == "Error")
                {
                    var msg = "Service returned an error";
                    if (root.TryGetProperty("payload", out var ep) &&
                        ep.TryGetProperty("message", out var mel))
                        msg = mel.GetString() ?? msg;
                    tcs.TrySetException(new InvalidOperationException(msg));
                }
                else
                {
                    JsonElement? payload = null;
                    if (root.TryGetProperty("payload", out var payloadEl) &&
                        payloadEl.ValueKind != JsonValueKind.Null)
                        payload = payloadEl.Clone();
                    tcs.TrySetResult(payload);
                }
            }
            else
            {
                JsonElement? payload = null;
                if (root.TryGetProperty("payload", out var payloadEl) &&
                    payloadEl.ValueKind != JsonValueKind.Null)
                    payload = payloadEl.Clone();

                var evt = new EventMessage { Type = type!, Payload = payload };
                EventReceived?.Invoke(this, evt);
            }
        }
        catch { }
    }

    // --- Nested converter ---

    private sealed class IPEndPointJsonConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (!root.TryGetProperty("address", out var addrEl) ||
                !root.TryGetProperty("port", out var portEl))
                return new IPEndPoint(IPAddress.Any, 0);

            return new IPEndPoint(
                IPAddress.Parse(addrEl.GetString() ?? "0.0.0.0"),
                portEl.GetInt32());
        }

        public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("address", value.Address.ToString());
            writer.WriteNumber("port", value.Port);
            writer.WriteEndObject();
        }
    }
}
