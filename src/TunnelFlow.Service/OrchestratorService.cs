using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TunnelFlow.Capture.Policy;
using TunnelFlow.Core;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;

namespace TunnelFlow.Service;

public sealed class OrchestratorService : BackgroundService
{
    private readonly ISingBoxManager _singBoxManager;
    private readonly ICaptureEngine _captureEngine;
    private readonly IPolicyEngine _policyEngine;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ConfigStore _configStore;
    private readonly PipeServer _pipeServer;
    private readonly ILogger<OrchestratorService> _logger;

    private TunnelFlowConfig _config = new();
    private bool _captureRunning;
    private int _singBoxRestartAttempts;
    private readonly SemaphoreSlim _captureLock = new(1, 1);

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TunnelFlow");

    public OrchestratorService(
        ISingBoxManager singBoxManager,
        ICaptureEngine captureEngine,
        IPolicyEngine policyEngine,
        ISessionRegistry sessionRegistry,
        ConfigStore configStore,
        PipeServer pipeServer,
        ILogger<OrchestratorService> logger)
    {
        _singBoxManager = singBoxManager;
        _captureEngine = captureEngine;
        _policyEngine = policyEngine;
        _sessionRegistry = sessionRegistry;
        _configStore = configStore;
        _pipeServer = pipeServer;
        _logger = logger;

        WirePipeHandlers();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _config = await _configStore.LoadAsync();
        _logger.LogInformation("Config loaded: {RuleCount} rules, {ProfileCount} profiles",
            _config.Rules.Count, _config.Profiles.Count);

        WireEvents();

        var pipeTask = _pipeServer.StartAsync(stoppingToken);

        if (_config.StartCaptureOnServiceStart && _config.ActiveProfileId is not null)
        {
            try { await StartCaptureAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "Auto-start capture failed"); }
        }

        PushCurrentStatus();
        await pipeTask;
    }

    public async Task StartCaptureAsync(CancellationToken ct = default)
    {
        await _captureLock.WaitAsync(ct);
        try
        {
            if (_captureRunning)
            {
                _logger.LogWarning("StartCapture called but capture already running");
                return;
            }

            var profile = _config.Profiles.FirstOrDefault(p => p.Id == _config.ActiveProfileId);
            if (profile is null)
            {
                _logger.LogError("No active profile configured");
                return;
            }

            var singBoxConfig = BuildSingBoxConfig();
            await _singBoxManager.StartAsync(profile, singBoxConfig, ct);

            var captureConfig = await BuildCaptureConfigAsync(profile, singBoxConfig, ct);
            await _captureEngine.StartAsync(captureConfig, ct);

            _captureRunning = true;
            _singBoxRestartAttempts = 0;
            _logger.LogInformation("Capture started");
            PushCurrentStatus();
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task StopCaptureAsync(CancellationToken ct = default)
    {
        await _captureLock.WaitAsync(ct);
        try
        {
            if (!_captureRunning)
                return;

            await _captureEngine.StopAsync(ct);
            await _singBoxManager.StopAsync(ct);

            _captureRunning = false;
            _logger.LogInformation("Capture stopped");
            PushCurrentStatus();
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private void WireEvents()
    {
        _singBoxManager.StatusChanged += (_, status) =>
        {
            switch (status)
            {
                case SingBoxStatus.Restarting:
                    _singBoxRestartAttempts++;
                    _pipeServer.PushSingBoxCrashed(
                        _singBoxRestartAttempts,
                        retryingInSeconds: (int)(_config.StartCaptureOnServiceStart ? 3 : 3));
                    break;

                case SingBoxStatus.Running:
                    _singBoxRestartAttempts = 0;
                    break;

                case SingBoxStatus.Crashed:
                    _logger.LogCritical("sing-box permanently crashed — stopping capture (fail-closed)");
                    _ = StopCaptureAsync();
                    break;
            }

            PushCurrentStatus();
        };

        _singBoxManager.LogLine += (_, line) =>
            _pipeServer.PushLogLine("singbox", "Info", line);

        _captureEngine.SessionCreated += (_, entry) =>
            _pipeServer.PushSessionCreated(entry);

        _captureEngine.SessionClosed += (_, entry) =>
            _pipeServer.PushSessionClosed(entry.FlowId);

        _captureEngine.ErrorOccurred += (_, err) =>
        {
            _logger.LogError("Capture error [{Code}]: {Message}", err.Code, err.Message);
            _pipeServer.PushLogLine("capture", "Error", $"[{err.Code}] {err.Message}");
        };
    }

    private void WirePipeHandlers()
    {
        _pipeServer.GetStateHandler = () => Task.FromResult(BuildStatePayload());

        _pipeServer.UpsertRuleHandler = async rule =>
        {
            var existing = _config.Rules.FindIndex(r => r.Id == rule.Id);
            if (existing >= 0) _config.Rules[existing] = rule;
            else _config.Rules.Add(rule);
            await _configStore.SaveAsync(_config);
            _policyEngine.UpdateRules(_config.Rules);
        };

        _pipeServer.DeleteRuleHandler = async ruleId =>
        {
            _config.Rules.RemoveAll(r => r.Id == ruleId);
            await _configStore.SaveAsync(_config);
            _policyEngine.UpdateRules(_config.Rules);
        };

        _pipeServer.UpsertProfileHandler = async profile =>
        {
            var existing = _config.Profiles.FindIndex(p => p.Id == profile.Id);
            if (existing >= 0) _config.Profiles[existing] = profile;
            else _config.Profiles.Add(profile);
            await _configStore.SaveAsync(_config);
        };

        _pipeServer.ActivateProfileHandler = async profileId =>
        {
            if (_config.Profiles.All(p => p.Id != profileId))
                throw new InvalidOperationException($"Profile {profileId} not found");

            _config.ActiveProfileId = profileId;
            await _configStore.SaveAsync(_config);

            if (_captureRunning)
            {
                await StopCaptureAsync();
                await StartCaptureAsync();
            }
        };

        _pipeServer.StartCaptureHandler = () => StartCaptureAsync();
        _pipeServer.StopCaptureHandler = () => StopCaptureAsync();

        _pipeServer.GetSessionsHandler = () =>
            Task.FromResult(_captureEngine.GetActiveSessions());
    }

    private StatePayload BuildStatePayload() => new()
    {
        Rules = _config.Rules,
        Profiles = _config.Profiles,
        ActiveProfileId = _config.ActiveProfileId,
        CaptureRunning = _captureRunning,
        SingBoxStatus = _singBoxManager.GetStatus()
    };

    private void PushCurrentStatus() =>
        _pipeServer.PushStatusChanged(_captureRunning, _singBoxManager.GetStatus());

    private SingBoxConfig BuildSingBoxConfig() => new()
    {
        SocksPort = _config.SocksPort,
        BinaryPath = Path.Combine(AppContext.BaseDirectory, "third_party", "singbox", "sing-box.exe"),
        ConfigOutputPath = Path.Combine(DataDir, "singbox-config.json"),
        LogOutputPath = Path.Combine(DataDir, "singbox.log"),
        RestartDelay = TimeSpan.FromSeconds(3),
        MaxRestartAttempts = 5
    };

    private async Task<CaptureConfig> BuildCaptureConfigAsync(
        VlessProfile profile,
        SingBoxConfig singBoxConfig,
        CancellationToken ct)
    {
        // Self-exclusion: sing-box binary + this service binary (non-negotiable per CURSOR_RULES.md §1)
        var excludedPaths = new List<string>
        {
            singBoxConfig.BinaryPath,
            Environment.ProcessPath ?? string.Empty
        };

        // Excluded destinations: VLESS server IP(s)
        var excludedDestinations = new List<IPAddress>();
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(profile.ServerAddress, ct);
            excludedDestinations.AddRange(addresses);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve server address {Address} for exclusion list",
                profile.ServerAddress);
        }

        return new CaptureConfig
        {
            SocksPort = _config.SocksPort,
            SocksAddress = IPAddress.Loopback,
            Rules = _config.Rules,
            ExcludedProcessPaths = excludedPaths,
            ExcludedDestinations = excludedDestinations
        };
    }
}
