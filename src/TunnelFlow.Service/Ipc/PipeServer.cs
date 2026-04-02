using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core.IPC.Messages;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.Ipc;

public sealed class PipeServer
{
    private const string PipeName = "TunnelFlowService";

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new IPEndPointJsonConverter()
        }
    };

    private static readonly JsonSerializerOptions _compactOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new IPEndPointJsonConverter()
        }
    };

    private readonly ILogger<PipeServer> _logger;
    private readonly ConcurrentDictionary<int, ConnectedClient> _clients = new();
    private int _nextClientId;

    // Command handlers wired by OrchestratorService
    public Func<Task<StatePayload>>? GetStateHandler { get; set; }
    public Func<AppRule, Task>? UpsertRuleHandler { get; set; }
    public Func<Guid, Task>? DeleteRuleHandler { get; set; }
    public Func<VlessProfile, Task>? UpsertProfileHandler { get; set; }
    public Func<Guid, Task>? ActivateProfileHandler { get; set; }
    public Func<Task>? StartCaptureHandler { get; set; }
    public Func<Task>? StopCaptureHandler { get; set; }
    public Func<Task<IReadOnlyList<SessionEntry>>>? GetSessionsHandler { get; set; }

    public PipeServer(ILogger<PipeServer> logger) => _logger = logger;

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Named pipe server starting on {PipeName}", PipeName);

        while (!ct.IsCancellationRequested)
        {

            // Apply PipeSecurity to every instance.
            // WorldSid:ReadWrite    — lets the non-elevated UI connect.
            // Current user FullControl — lets the elevated service create additional
            //   instances. Without FullControl for the creator the explicit DACL
            //   strips CreateNewInstance rights and every instance after the first
            //   throws UnauthorizedAccessException.
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                WindowsIdentity.GetCurrent().User!,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            var pipe = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 0,
                outBufferSize: 0,
                pipeSecurity: security);

            try
            {
                await pipe.WaitForConnectionAsync(ct);
                _ = HandleClientAsync(pipe, ct);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe accept error");
                pipe.Dispose();
            }
        }
    }

    // --- Push event methods ---

    public void PushStatusChanged(StatusPayload status) =>
        BroadcastEvent("StatusChanged", JsonSerializer.SerializeToNode(status, _compactOptions));

    public void PushLogLine(string source, string level, string message)
    {
        var payload = new JsonObject
        {
            ["source"] = source,
            ["level"] = level,
            ["message"] = message
        };
        BroadcastEvent("LogLine", payload);
    }

    public void PushSessionCreated(SessionEntry entry)
    {
        var payload = JsonSerializer.SerializeToNode(entry, _compactOptions);
        BroadcastEvent("SessionCreated", payload);
    }

    public void PushSessionClosed(ulong flowId)
    {
        var payload = new JsonObject { ["flowId"] = flowId };
        BroadcastEvent("SessionClosed", payload);
    }

    public void PushSingBoxCrashed(int attempt, int retryingInSeconds)
    {
        var payload = new JsonObject
        {
            ["attempt"] = attempt,
            ["retryingIn"] = retryingInSeconds
        };
        BroadcastEvent("SingBoxCrashed", payload);
    }

    // --- Private implementation ---

    private void BroadcastEvent(string type, JsonNode? payload)
    {
        var envelope = new JsonObject { ["type"] = type, ["payload"] = payload?.DeepClone() };
        var json = envelope.ToJsonString(_compactOptions);

        foreach (var (_, client) in _clients)
        {
            client.Outbound.Writer.TryWrite(json);
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        int id = Interlocked.Increment(ref _nextClientId);

        var outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        var client = new ConnectedClient(id, outbound);
        _clients.TryAdd(id, client);
        _logger.LogDebug("Pipe client {Id} connected", id);

        using var clientLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var readerTask = Task.Run(async () =>
        {
            try { await ClientReaderAsync(pipe, outbound, clientLifetime.Token); }
            finally { clientLifetime.Cancel(); }
        });

        var writerTask = Task.Run(async () =>
        {
            try { await ClientWriterAsync(pipe, outbound, clientLifetime.Token); }
            finally { clientLifetime.Cancel(); }
        });

        await Task.WhenAll(readerTask, writerTask);

        _clients.TryRemove(id, out _);
        outbound.Writer.TryComplete();
        _logger.LogDebug("Pipe client {Id} disconnected", id);
        pipe.Dispose();
    }

    private static async Task ClientWriterAsync(
        NamedPipeServerStream pipe,
        Channel<string> outbound,
        CancellationToken ct)
    {
        var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        await foreach (var msg in outbound.Reader.ReadAllAsync(ct))
        {
            await writer.WriteAsync(msg + "\n");
        }
    }

    private async Task ClientReaderAsync(
        NamedPipeServerStream pipe,
        Channel<string> outbound,
        CancellationToken ct)
    {
        var reader = new StreamReader(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                await ProcessCommandAsync(line, outbound, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pipe client reader error");
        }
    }

    private async Task ProcessCommandAsync(
        string line,
        Channel<string> outbound,
        CancellationToken ct)
    {
        CommandMessage? cmd;
        try
        {
            cmd = JsonSerializer.Deserialize<CommandMessage>(line, _serializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed IPC message: {Line}", line);
            return;
        }

        if (cmd is null) return;

        try
        {
            var responseJson = await DispatchAsync(cmd, ct);
            outbound.Writer.TryWrite(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Handler error for command {Type}", cmd.Type);
            outbound.Writer.TryWrite(MakeError(cmd.Id, "HANDLER_ERROR", ex.Message));
        }
    }

    private async Task<string> DispatchAsync(CommandMessage cmd, CancellationToken ct)
    {
        switch (cmd.Type)
        {
            case "GetState":
            {
                if (GetStateHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                var state = await GetStateHandler();
                return MakeOk(cmd.Id, JsonSerializer.SerializeToNode(state, _serializerOptions));
            }

            case "UpsertRule":
            {
                if (UpsertRuleHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                if (cmd.Payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Payload required");
                var rule = JsonSerializer.Deserialize<AppRule>(cmd.Payload.Value, _serializerOptions);
                if (rule is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Invalid AppRule");
                await UpsertRuleHandler(rule);
                return MakeOk(cmd.Id, null);
            }

            case "DeleteRule":
            {
                if (DeleteRuleHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                if (cmd.Payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Payload required");
                var payload = JsonSerializer.Deserialize<DeleteRulePayload>(cmd.Payload.Value, _serializerOptions);
                if (payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Invalid payload");
                await DeleteRuleHandler(payload.RuleId);
                return MakeOk(cmd.Id, null);
            }

            case "UpsertProfile":
            {
                if (UpsertProfileHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                if (cmd.Payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Payload required");
                var profile = JsonSerializer.Deserialize<VlessProfile>(cmd.Payload.Value, _serializerOptions);
                if (profile is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Invalid VlessProfile");
                await UpsertProfileHandler(profile);
                return MakeOk(cmd.Id, null);
            }

            case "ActivateProfile":
            {
                if (ActivateProfileHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                if (cmd.Payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Payload required");
                var payload = JsonSerializer.Deserialize<ActivateProfilePayload>(cmd.Payload.Value, _serializerOptions);
                if (payload is null) return MakeError(cmd.Id, "BAD_PAYLOAD", "Invalid payload");
                await ActivateProfileHandler(payload.ProfileId);
                return MakeOk(cmd.Id, null);
            }

            case "StartCapture":
            {
                if (StartCaptureHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                await StartCaptureHandler();
                return MakeOk(cmd.Id, null);
            }

            case "StopCapture":
            {
                if (StopCaptureHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                await StopCaptureHandler();
                return MakeOk(cmd.Id, null);
            }

            case "GetSessions":
            {
                if (GetSessionsHandler is null) return MakeError(cmd.Id, "NOT_READY", "Service not ready");
                var sessions = await GetSessionsHandler();
                return MakeOk(cmd.Id, JsonSerializer.SerializeToNode(sessions, _serializerOptions));
            }

            default:
                return MakeError(cmd.Id, "UNKNOWN_COMMAND", $"Unknown command type: {cmd.Type}");
        }
    }

    private static string MakeOk(string id, JsonNode? payload)
    {
        var obj = new JsonObject
        {
            ["type"] = "Ok",
            ["id"] = id,
            ["payload"] = payload
        };
        return obj.ToJsonString(_compactOptions);
    }

    private static string MakeError(string id, string code, string message)
    {
        var obj = new JsonObject
        {
            ["type"] = "Error",
            ["id"] = id,
            ["payload"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        return obj.ToJsonString(_compactOptions);
    }

    private sealed record ConnectedClient(int Id, Channel<string> Outbound);
}

internal sealed class IPEndPointJsonConverter : JsonConverter<IPEndPoint>
{
    public override IPEndPoint? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var address = IPAddress.Parse(
            root.GetProperty("address").GetString() ?? "0.0.0.0");
        var port = root.GetProperty("port").GetInt32();
        return new IPEndPoint(address, port);
    }

    public override void Write(
        Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("address", value.Address.ToString());
        writer.WriteNumber("port", value.Port);
        writer.WriteEndObject();
    }
}
