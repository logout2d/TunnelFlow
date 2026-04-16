using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.IPC.Messages;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;
using TunnelFlow.Service.Tun;

namespace TunnelFlow.Service;

public sealed class OrchestratorService : BackgroundService
{
    private readonly ISingBoxManager _singBoxManager;
    private readonly ITunOrchestrator _tunOrchestrator;
    private readonly ConfigStore _configStore;
    private readonly PipeServer _pipeServer;
    private readonly ILogger<OrchestratorService> _logger;

    private TunnelFlowConfig _config = new();
    private TunnelLifecycleState _lifecycleState = TunnelLifecycleState.Stopped;
    private bool _tunModeActive;
    private TunnelMode _selectedMode = TunnelMode.Legacy;
    private RuntimeWarningEvidence _runtimeWarning = RuntimeWarningEvidence.None;
    private RuntimeWarningStrength _runtimeWarningStrength = RuntimeWarningStrength.None;
    private int _weakConnectionEvidenceCount;
    private int _singBoxRestartAttempts;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly object _ownerLeaseGate = new();
    private CancellationToken _stoppingToken;
    private string? _activeOwnerSessionId;
    private DateTimeOffset? _activeOwnerHeartbeatUtc;

    internal static readonly TimeSpan OwnerHeartbeatInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan OwnerLeaseTimeout = TimeSpan.FromSeconds(15);

    private static readonly string DataDir = RuntimePaths.DefaultDataRoot;

    public OrchestratorService(
        ISingBoxManager singBoxManager,
        ITunOrchestrator tunOrchestrator,
        ConfigStore configStore,
        PipeServer pipeServer,
        ILogger<OrchestratorService> logger)
    {
        _singBoxManager = singBoxManager;
        _tunOrchestrator = tunOrchestrator;
        _configStore = configStore;
        _pipeServer = pipeServer;
        _logger = logger;

        WirePipeHandlers();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;

        _config = await _configStore.LoadAsync();
        _logger.LogInformation("Config loaded: {RuleCount} rules, {ProfileCount} profiles",
            _config.Rules.Count, _config.Profiles.Count);

        WireEvents();

        var pipeTask = _pipeServer.StartAsync(stoppingToken);
        var ownerLeaseTask = MonitorOwnerLeaseAsync(stoppingToken);

        if (_config.StartCaptureOnServiceStart && _config.ActiveProfileId is not null)
        {
            try { await StartCaptureAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Auto-start capture failed"); }
        }

        PushCurrentStatus();
        await Task.WhenAll(pipeTask, ownerLeaseTask);
    }

    public async Task StartCaptureAsync(string? ownerSessionId = null)
    {
        var requestedState = ReadLifecycleState();
        if (!CanStartCapture(requestedState))
        {
            _pipeServer.PushLogLine("service", "Warning", GetStartBlockedReason(requestedState));
            return;
        }

        await _captureLock.WaitAsync(_stoppingToken);
        try
        {
            var lifecycleState = ReadLifecycleState();
            if (!CanStartCapture(lifecycleState))
            {
                _pipeServer.PushLogLine("service", "Warning", GetStartBlockedReason(lifecycleState));
                return;
            }

            SetLifecycleState(TunnelLifecycleState.Starting);
            ResetRuntimeWarningEvidence("start-session");
            _pipeServer.PushLogLine("service", "Info", "Starting tunnel...");
            PushCurrentStatus();

            var config = await _configStore.LoadAsync();
            _config = config;

            if (config.ActiveProfileId is null)
            {
                AbortStart("Cannot start: no active VLESS profile selected.");
                return;
            }

            var profile = config.Profiles.FirstOrDefault(p => p.Id == config.ActiveProfileId);
            if (profile is null)
            {
                AbortStart("Cannot start: active profile not found in config.");
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.ServerAddress) ||
                string.IsNullOrWhiteSpace(profile.UserId))
            {
                AbortStart("Cannot start: profile is missing ServerAddress or UserId.");
                return;
            }

            Directory.CreateDirectory(DataDir);

            var singBoxExe = ResolveSingBoxPath();
            var tunModeSelection = TunModeSelector.Select(
                config.UseTunMode,
                _tunOrchestrator.SupportsActivation,
                _tunOrchestrator.ResolvedWintunPath);

            if (!File.Exists(singBoxExe))
            {
                AbortStart($"sing-box binary not found at: {singBoxExe}");
                _pipeServer.PushLogLine("service", "Error",
                    "Download sing-box.exe and place it in third_party/singbox/");
                return;
            }

            _logger.LogInformation(
                "Tunnel mode selection requestedTunMode={RequestedTunMode} selectedMode={SelectedMode} tunPrerequisitesSatisfied={TunPrerequisitesSatisfied} tunActivationSupported={TunActivationSupported} selectionReason={SelectionReason} wintunPath={WintunPath}",
                tunModeSelection.UseTunModeRequested,
                tunModeSelection.SelectedMode,
                tunModeSelection.TunPrerequisitesSatisfied,
                tunModeSelection.TunActivationSupported,
                tunModeSelection.SelectionReason,
                tunModeSelection.WintunPath);

            _pipeServer.PushLogLine(
                "service",
                "Info",
                $"Tunnel mode selection: requestedTun={tunModeSelection.UseTunModeRequested}, selectedMode={tunModeSelection.SelectedMode}, tunPrerequisitesSatisfied={tunModeSelection.TunPrerequisitesSatisfied}, wintunPath={tunModeSelection.WintunPath}");

            var tunOnlyStartBlockReason = GetTunOnlyStartBlockReason(tunModeSelection);
            if (tunOnlyStartBlockReason is not null)
            {
                _logger.LogWarning(
                    "TUN-only runtime blocked start requestedTunMode={RequestedTunMode} selectedMode={SelectedMode} selectionReason={SelectionReason} wintunPath={WintunPath}",
                    tunModeSelection.UseTunModeRequested,
                    tunModeSelection.SelectedMode,
                    tunModeSelection.SelectionReason,
                    tunModeSelection.WintunPath);
                AbortStart(tunOnlyStartBlockReason);
                return;
            }

            var runtimePaths = RuntimePaths.Current;
            var logDir = runtimePaths.CurrentLogsRoot;
            Directory.CreateDirectory(logDir);

            try
            {
                _pipeServer.PushLogLine(
                    "service",
                    "Info",
                    $"TUN activation attempt: wintunPath={tunModeSelection.WintunPath}");
                await _tunOrchestrator.StartAsync(
                    new TunOrchestrationConfig
                    {
                        UseTunMode = true,
                        WintunPath = tunModeSelection.WintunPath
                    },
                    _stoppingToken);
                _tunModeActive = true;
                _pipeServer.PushLogLine(
                    "service",
                    "Info",
                    $"TUN activation succeeded: wintunPath={tunModeSelection.WintunPath}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "TUN activation failed in TUN-only runtime wintunPath={WintunPath}",
                    tunModeSelection.WintunPath);
                AbortStart($"Cannot start: TUN activation failed: {ex.Message}");
                return;
            }

            _selectedMode = TunnelMode.Tun;

            var tunPolicySummaries = BuildTunPolicySummaries(config.Rules);
            _logger.LogInformation(
                "TUN policy summary count={Count}",
                tunPolicySummaries.Count);
            _pipeServer.PushLogLine(
                "service",
                "Info",
                $"TUN policy summary count={tunPolicySummaries.Count}");

            foreach (var summary in tunPolicySummaries)
            {
                _logger.LogInformation(
                    "TUN policy appPath={AppPath} ruleMode={RuleMode} mappedAction={MappedAction} mappedOutbound={MappedOutbound}",
                    summary.AppPath,
                    summary.RuleMode,
                    summary.MappedAction,
                    summary.MappedOutbound ?? "(none)");
                _pipeServer.PushLogLine(
                    "service",
                    "Info",
                    $"TUN policy: appPath={summary.AppPath}, ruleMode={summary.RuleMode}, mappedAction={summary.MappedAction}, mappedOutbound={summary.MappedOutbound ?? "(none)"}");
            }

            var singBoxConfig = new SingBoxConfig
            {
                SocksPort = config.SocksPort,
                UseTunMode = true,
                Rules = config.Rules,
                BinaryPath = singBoxExe,
                ConfigOutputPath = runtimePaths.SingBoxConfigPath,
                LogOutputPath = runtimePaths.SingBoxLogPath,
                RestartDelay = TimeSpan.FromSeconds(3),
                MaxRestartAttempts = 5
            };

            _pipeServer.PushLogLine("service", "Info", $"Starting sing-box: {singBoxExe}");
            await _singBoxManager.StartAsync(profile, singBoxConfig, _stoppingToken);

            SetLifecycleState(TunnelLifecycleState.Running);
            AcquireTunnelOwner(ownerSessionId);
            _singBoxRestartAttempts = 0;
            _pipeServer.PushLogLine("service", "Info", "Tunnel started successfully.");
            PushCurrentStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StartCaptureAsync failed; beginning fail-closed startup cleanup");
            _pipeServer.PushLogLine("service", "Warning",
                "Startup failed; beginning fail-closed cleanup.");

            try
            {
                await SafeStopSingBoxAsync();
                if (_tunModeActive)
                {
                    _logger.LogInformation(
                        "Startup failure cleanup: stopping TUN after sing-box startup failure");
                    _pipeServer.PushLogLine(
                        "service",
                        "Info",
                        "Startup failure cleanup: stopping TUN.");
                    await _tunOrchestrator.StopAsync(_stoppingToken);
                    _tunModeActive = false;
                }
            }
            catch { }
            SetLifecycleState(TunnelLifecycleState.Stopped);
            ClearTunnelOwner("startup-failed");
            _pipeServer.PushLogLine("service", "Error",
                $"StartCaptureAsync failed: {ex.GetType().Name}: {ex.Message}");
            PushCurrentStatus();
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task StopCaptureAsync()
    {
        var requestedState = ReadLifecycleState();
        if (!CanStopCapture(requestedState))
        {
            _pipeServer.PushLogLine("service", "Warning", GetStopBlockedReason(requestedState));
            return;
        }

        await _captureLock.WaitAsync(_stoppingToken);
        try
        {
            var lifecycleState = ReadLifecycleState();
            if (!CanStopCapture(lifecycleState))
            {
                _pipeServer.PushLogLine("service", "Warning", GetStopBlockedReason(lifecycleState));
                return;
            }

            SetLifecycleState(TunnelLifecycleState.Stopping);
            _pipeServer.PushLogLine("service", "Info", "Stopping tunnel...");
            PushCurrentStatus();

            await _singBoxManager.StopAsync(_stoppingToken);
            if (_tunModeActive)
            {
                await _tunOrchestrator.StopAsync(_stoppingToken);
                _tunModeActive = false;
            }

            SetLifecycleState(TunnelLifecycleState.Stopped);
            ResetRuntimeWarningEvidence("stop-session");
            ClearTunnelOwner("stop-session");
            _pipeServer.PushLogLine("service", "Info", "Tunnel stopped.");
            PushCurrentStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StopCaptureAsync failed");
            _pipeServer.PushLogLine("service", "Error",
                $"StopCaptureAsync failed: {ex.GetType().Name}: {ex.Message}");
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
            _pipeServer.PushLogLine("service", "Info", $"sing-box status changed: {status}");

            if (IsRuntimeWarningResetBoundary(status))
            {
                ResetRuntimeWarningEvidence($"singbox-status:{status}");
            }

            switch (status)
            {
                case SingBoxStatus.Restarting:
                    _singBoxRestartAttempts++;
                    _pipeServer.PushSingBoxCrashed(_singBoxRestartAttempts, retryingInSeconds: 3);
                    break;

                case SingBoxStatus.Running:
                    _singBoxRestartAttempts = 0;
                    break;

                case SingBoxStatus.Crashed:
                    _logger.LogCritical(
                        "sing-box unexpectedly exited after startup stabilization - stopping capture for fail-closed cleanup");
                    _pipeServer.PushLogLine(
                        "service",
                        "Warning",
                        "sing-box exited unexpectedly after startup; stopping tunnel for cleanup.");
                    _ = StopCaptureAsync();
                    break;
            }

            PushCurrentStatus();
        };

        _singBoxManager.LogLine += (_, line) =>
        {
            if (HandleRuntimeWarningEvidence(line))
            {
                PushCurrentStatus();
            }

            _pipeServer.PushLogLine("singbox", "Info", line);
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
        };

        _pipeServer.DeleteRuleHandler = async ruleId =>
        {
            _config.Rules.RemoveAll(r => r.Id == ruleId);
            await _configStore.SaveAsync(_config);
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

            if (ReadLifecycleState() == TunnelLifecycleState.Running)
            {
                var ownerSessionId = GetActiveOwnerSessionId();
                await StopCaptureAsync();
                await StartCaptureAsync(ownerSessionId);
            }
        };

        _pipeServer.DeleteProfileHandler = async profileId =>
        {
            var removed = _config.Profiles.RemoveAll(p => p.Id == profileId);
            if (removed == 0)
                throw new InvalidOperationException($"Profile {profileId} not found");

            if (_config.ActiveProfileId == profileId)
            {
                _config.ActiveProfileId = _config.Profiles.FirstOrDefault()?.Id;
            }

            await _configStore.SaveAsync(_config);
        };

        _pipeServer.StartCaptureHandler = ownerSessionId => StartCaptureAsync(ownerSessionId);
        _pipeServer.StopCaptureHandler = () => StopCaptureAsync();
        _pipeServer.OwnerHeartbeatHandler = ownerSessionId =>
        {
            RefreshTunnelOwnerHeartbeat(ownerSessionId);
            return Task.CompletedTask;
        };
    }

    private StatePayload BuildStatePayload()
    {
        var summary = BuildStatusSummary();
        return new StatePayload
        {
            Rules = _config.Rules,
            Profiles = _config.Profiles,
            ActiveProfileId = summary.ActiveProfileId,
            ActiveProfileName = summary.ActiveProfileName,
            ActiveOwnerSessionId = summary.ActiveOwnerSessionId,
            CaptureRunning = summary.CaptureRunning,
            LifecycleState = summary.LifecycleState,
            SingBoxStatus = summary.SingBoxStatus,
            SelectedMode = summary.SelectedMode,
            SingBoxRunning = summary.SingBoxRunning,
            TunnelInterfaceUp = summary.TunnelInterfaceUp,
            ProxyRuleCount = summary.ProxyRuleCount,
            DirectRuleCount = summary.DirectRuleCount,
            BlockRuleCount = summary.BlockRuleCount,
            RuntimeWarning = summary.RuntimeWarning
        };
    }

    private void PushCurrentStatus()
    {
        var payload = ToStatusPayload(BuildStatusSummary());
        if (payload.RuntimeWarning != RuntimeWarningEvidence.None)
        {
            _pipeServer.PushLogLine(
                "service",
                "Debug",
                $"runtime-warning diag: push warning={payload.RuntimeWarning}, singBoxStatus={payload.SingBoxStatus}, captureRunning={payload.CaptureRunning}, tunnelInterfaceUp={payload.TunnelInterfaceUp}");
        }

        _pipeServer.PushStatusChanged(payload);
    }

    private ServiceStatusSummary BuildStatusSummary()
    {
        var lifecycleState = ReadLifecycleState();
        var selectedMode = GetEffectiveStatusMode(lifecycleState);
        var summary = BuildStatusSummary(
            _config,
            lifecycleState,
            selectedMode,
            _tunModeActive,
            _singBoxManager.GetStatus(),
            _runtimeWarning);

        return summary with { ActiveOwnerSessionId = GetActiveOwnerSessionId() };
    }

    private TunnelMode GetEffectiveStatusMode(TunnelLifecycleState lifecycleState)
    {
        if (lifecycleState != TunnelLifecycleState.Stopped)
            return _selectedMode;

        return TunModeSelector.Select(
            _config.UseTunMode,
            _tunOrchestrator.SupportsActivation,
            _tunOrchestrator.ResolvedWintunPath).SelectedMode;
    }

    private static string ResolveSingBoxPath()
    {
        var runtimePaths = RuntimePaths.Current;
        var candidates = runtimePaths.GetSingBoxExecutableCandidates();
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    internal static string? GetTunOnlyStartBlockReason(TunModeSelectionResult selection)
    {
        if (selection.SelectedMode == TunnelMode.Tun)
        {
            return null;
        }

        if (!selection.UseTunModeRequested)
        {
            return "Cannot start: TUN-only runtime requires UseTunMode=true.";
        }

        return $"Cannot start: TUN-only runtime prerequisites not met ({selection.SelectionReason}).";
    }

    internal static ServiceStatusSummary BuildStatusSummary(
        TunnelFlowConfig config,
        TunnelLifecycleState lifecycleState,
        TunnelMode selectedMode,
        bool tunModeActive,
        SingBoxStatus singBoxStatus,
        RuntimeWarningEvidence runtimeWarning = RuntimeWarningEvidence.None)
    {
        var activeProfile = config.ActiveProfileId is null
            ? null
            : config.Profiles.FirstOrDefault(profile => profile.Id == config.ActiveProfileId);

        var proxyRuleCount = 0;
        var directRuleCount = 0;
        var blockRuleCount = 0;

        foreach (var rule in config.Rules.Where(rule =>
                     rule.IsEnabled &&
                     !string.IsNullOrWhiteSpace(rule.ExePath)))
        {
            switch (rule.Mode)
            {
                case RuleMode.Proxy:
                    proxyRuleCount++;
                    break;
                case RuleMode.Direct:
                    directRuleCount++;
                    break;
                case RuleMode.Block:
                    blockRuleCount++;
                    break;
            }
        }

        return new ServiceStatusSummary(
            SelectedMode: MapTunnelStatusMode(selectedMode),
            CaptureRunning: lifecycleState is TunnelLifecycleState.Running or TunnelLifecycleState.Stopping,
            LifecycleState: lifecycleState,
            SingBoxStatus: singBoxStatus,
            SingBoxRunning: singBoxStatus == SingBoxStatus.Running,
            TunnelInterfaceUp: selectedMode == TunnelMode.Tun &&
                               tunModeActive &&
                               singBoxStatus == SingBoxStatus.Running,
            ActiveProfileId: config.ActiveProfileId,
            ActiveProfileName: activeProfile?.Name,
            ActiveOwnerSessionId: null,
            ProxyRuleCount: proxyRuleCount,
            DirectRuleCount: directRuleCount,
            BlockRuleCount: blockRuleCount,
            RuntimeWarning: runtimeWarning);
    }

    private static StatusPayload ToStatusPayload(ServiceStatusSummary summary) => new()
    {
        CaptureRunning = summary.CaptureRunning,
        LifecycleState = summary.LifecycleState,
        SingBoxStatus = summary.SingBoxStatus,
        SelectedMode = summary.SelectedMode,
        SingBoxRunning = summary.SingBoxRunning,
        TunnelInterfaceUp = summary.TunnelInterfaceUp,
        ActiveProfileId = summary.ActiveProfileId,
        ActiveProfileName = summary.ActiveProfileName,
        ActiveOwnerSessionId = summary.ActiveOwnerSessionId,
        ProxyRuleCount = summary.ProxyRuleCount,
        DirectRuleCount = summary.DirectRuleCount,
        BlockRuleCount = summary.BlockRuleCount,
        RuntimeWarning = summary.RuntimeWarning
    };

    private static TunnelStatusMode MapTunnelStatusMode(TunnelMode selectedMode) =>
        selectedMode == TunnelMode.Tun
            ? TunnelStatusMode.Tun
            : TunnelStatusMode.Legacy;

    internal static IReadOnlyList<TunPolicySummary> BuildTunPolicySummaries(IReadOnlyList<AppRule> rules) =>
        rules
            .Where(rule => rule.IsEnabled && !string.IsNullOrWhiteSpace(rule.ExePath))
            .Select(rule => rule.Mode switch
            {
                RuleMode.Proxy => new TunPolicySummary(rule.ExePath, rule.Mode, "route", "vless-out"),
                RuleMode.Direct => new TunPolicySummary(rule.ExePath, rule.Mode, "route", "direct"),
                RuleMode.Block => new TunPolicySummary(rule.ExePath, rule.Mode, "reject", null),
                _ => new TunPolicySummary(rule.ExePath, rule.Mode, "route", "direct")
            })
            .ToArray();

    internal static RuntimeWarningDetail ClassifyRuntimeWarningDetail(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return RuntimeWarningDetail.None;
        }

        var normalized = line.ToLowerInvariant();

        if (ContainsAny(
                normalized,
                "authentication failed",
                "message authentication failed",
                "invalid user",
                "invalid uuid",
                "unauthorized",
                "forbidden",
                "status code: 403",
                "user disabled",
                "account disabled",
                "permission denied"))
        {
            return RuntimeWarningDetail.AuthenticationFailure;
        }

        if (ContainsAny(
                normalized,
                "connection refused",
                "network is unreachable",
                "host is unreachable",
                "no route to host",
                "i/o timeout",
                "timed out",
                "context deadline exceeded",
                "handshake failure",
                "remote error: tls",
                "bad certificate",
                "first record does not look like a tls handshake"))
        {
            return RuntimeWarningDetail.StrongConnectionProblem;
        }

        if (ContainsAny(
                normalized,
                "connection reset by peer",
                "forcibly closed by the remote host",
                "wsarecv",
                "download closed",
                "upload closed",
                "packet download closed",
                "packet upload closed"))
        {
            return RuntimeWarningDetail.WeakConnectionNoise;
        }

        return RuntimeWarningDetail.None;
    }

    internal static RuntimeWarningEvidence MapRuntimeWarningEvidence(RuntimeWarningDetail detail) => detail switch
    {
        RuntimeWarningDetail.AuthenticationFailure => RuntimeWarningEvidence.AuthenticationFailure,
        RuntimeWarningDetail.StrongConnectionProblem => RuntimeWarningEvidence.ConnectionProblem,
        RuntimeWarningDetail.WeakConnectionNoise => RuntimeWarningEvidence.ConnectionProblem,
        _ => RuntimeWarningEvidence.None
    };

    internal static RuntimeWarningStrength GetRuntimeWarningStrength(RuntimeWarningDetail detail) => detail switch
    {
        RuntimeWarningDetail.AuthenticationFailure => RuntimeWarningStrength.Strong,
        RuntimeWarningDetail.StrongConnectionProblem => RuntimeWarningStrength.Strong,
        RuntimeWarningDetail.WeakConnectionNoise => RuntimeWarningStrength.Weak,
        _ => RuntimeWarningStrength.None
    };

    internal const int WeakConnectionProblemThreshold = 2;

    internal static bool IsRuntimeWarningResetBoundary(SingBoxStatus status) => status switch
    {
        SingBoxStatus.Stopped => true,
        SingBoxStatus.Restarting => true,
        SingBoxStatus.Crashed => true,
        _ => false
    };

    internal static RuntimeWarningTracker ApplyRuntimeWarningEvidence(RuntimeWarningTracker current, string line)
    {
        var detail = ClassifyRuntimeWarningDetail(line);
        if (detail == RuntimeWarningDetail.None)
        {
            return current;
        }

        if (detail == RuntimeWarningDetail.WeakConnectionNoise)
        {
            if (current.Warning == RuntimeWarningEvidence.AuthenticationFailure &&
                current.Strength == RuntimeWarningStrength.Strong)
            {
                return current;
            }

            if (current.Warning == RuntimeWarningEvidence.ConnectionProblem &&
                current.Strength == RuntimeWarningStrength.Strong)
            {
                return current;
            }

            var nextWeakCount = Math.Min(current.WeakConnectionEvidenceCount + 1, WeakConnectionProblemThreshold);
            if (nextWeakCount < WeakConnectionProblemThreshold)
            {
                return current with { WeakConnectionEvidenceCount = nextWeakCount };
            }

            return current with
            {
                Warning = RuntimeWarningEvidence.ConnectionProblem,
                Strength = RuntimeWarningStrength.Weak,
                WeakConnectionEvidenceCount = nextWeakCount
            };
        }

        var warning = MapRuntimeWarningEvidence(detail);
        var strength = GetRuntimeWarningStrength(detail);

        if (warning == RuntimeWarningEvidence.AuthenticationFailure)
        {
            return current with
            {
                Warning = warning,
                Strength = strength,
                WeakConnectionEvidenceCount = 0
            };
        }

        if (current.Warning == RuntimeWarningEvidence.AuthenticationFailure &&
            current.Strength == RuntimeWarningStrength.Strong)
        {
            return current;
        }

        return current with
        {
            Warning = warning,
            Strength = strength,
            WeakConnectionEvidenceCount = 0
        };
    }

    internal static RuntimeWarningTracker ApplyRuntimeWarningResetBoundary(
        RuntimeWarningTracker current,
        SingBoxStatus status) =>
        IsRuntimeWarningResetBoundary(status)
            ? RuntimeWarningTracker.Empty
            : current;

    private bool HandleRuntimeWarningEvidence(string line)
    {
        var current = GetRuntimeWarningTracker();
        var next = ApplyRuntimeWarningEvidence(current, line);
        SetRuntimeWarningTracker(next);

        if (next.Warning == current.Warning && next.Strength == current.Strength)
        {
            return false;
        }

        var detail = ClassifyRuntimeWarningDetail(line);
        if (next.Warning != RuntimeWarningEvidence.None)
        {
            _pipeServer.PushLogLine(
                "service",
                "Debug",
                $"runtime-warning diag: set warning={next.Warning}, strength={next.Strength}, detail={detail}, weakCount={next.WeakConnectionEvidenceCount}");
        }

        return true;
    }

    internal static TunnelOwnerLeaseState AcquireOwnerLease(
        TunnelOwnerLeaseState current,
        string? ownerSessionId,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerSessionId))
        {
            return TunnelOwnerLeaseState.Empty;
        }

        return new TunnelOwnerLeaseState(ownerSessionId, nowUtc);
    }

    internal static TunnelOwnerLeaseState RefreshOwnerLease(
        TunnelOwnerLeaseState current,
        string ownerSessionId,
        TunnelLifecycleState lifecycleState,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(ownerSessionId) ||
            lifecycleState == TunnelLifecycleState.Stopped ||
            !string.Equals(current.OwnerSessionId, ownerSessionId, StringComparison.Ordinal))
        {
            return current;
        }

        return current with { LastHeartbeatUtc = nowUtc };
    }

    internal static bool HasOwnerLeaseExpired(
        TunnelOwnerLeaseState current,
        TunnelLifecycleState lifecycleState,
        DateTimeOffset nowUtc,
        TimeSpan timeout)
    {
        if (lifecycleState != TunnelLifecycleState.Running ||
            string.IsNullOrWhiteSpace(current.OwnerSessionId) ||
            current.LastHeartbeatUtc is null)
        {
            return false;
        }

        return nowUtc - current.LastHeartbeatUtc.Value >= timeout;
    }

    internal async Task HandleOwnerLeaseTickAsync(DateTimeOffset nowUtc)
    {
        TunnelOwnerLeaseState leaseState;
        lock (_ownerLeaseGate)
        {
            leaseState = new TunnelOwnerLeaseState(_activeOwnerSessionId, _activeOwnerHeartbeatUtc);
        }

        if (!HasOwnerLeaseExpired(leaseState, ReadLifecycleState(), nowUtc, OwnerLeaseTimeout))
        {
            return;
        }

        _logger.LogWarning(
            "Tunnel owner lease expired for {OwnerSessionId}; stopping tunnel",
            leaseState.OwnerSessionId);
        _pipeServer.PushLogLine(
            "service",
            "Warning",
            $"Tunnel owner lease expired for session {leaseState.OwnerSessionId}; stopping tunnel.");
        ClearTunnelOwner("lease-expired");
        await StopCaptureAsync();
    }

    private async Task MonitorOwnerLeaseAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await HandleOwnerLeaseTickAsync(DateTimeOffset.UtcNow);
        }
    }

    private void AcquireTunnelOwner(string? ownerSessionId)
    {
        var leaseState = AcquireOwnerLease(
            new TunnelOwnerLeaseState(_activeOwnerSessionId, _activeOwnerHeartbeatUtc),
            ownerSessionId,
            DateTimeOffset.UtcNow);

        if (string.IsNullOrWhiteSpace(leaseState.OwnerSessionId))
        {
            ClearTunnelOwner("start-without-owner");
            _logger.LogInformation("Tunnel started without an active owner lease");
            _pipeServer.PushLogLine("service", "Info", "Tunnel started without an active owner lease.");
            return;
        }

        lock (_ownerLeaseGate)
        {
            _activeOwnerSessionId = leaseState.OwnerSessionId;
            _activeOwnerHeartbeatUtc = leaseState.LastHeartbeatUtc;
        }

        _logger.LogInformation(
            "Tunnel owner acquired for session {OwnerSessionId}",
            leaseState.OwnerSessionId);
        _pipeServer.PushLogLine(
            "service",
            "Info",
            $"Tunnel owner acquired: session {leaseState.OwnerSessionId}");
    }

    private void RefreshTunnelOwnerHeartbeat(string ownerSessionId)
    {
        TunnelOwnerLeaseState current;
        lock (_ownerLeaseGate)
        {
            current = new TunnelOwnerLeaseState(_activeOwnerSessionId, _activeOwnerHeartbeatUtc);
            var refreshed = RefreshOwnerLease(current, ownerSessionId, ReadLifecycleState(), DateTimeOffset.UtcNow);
            _activeOwnerSessionId = refreshed.OwnerSessionId;
            _activeOwnerHeartbeatUtc = refreshed.LastHeartbeatUtc;
            current = refreshed;
        }

        if (string.Equals(current.OwnerSessionId, ownerSessionId, StringComparison.Ordinal))
        {
            _logger.LogDebug("Tunnel owner heartbeat refreshed for session {OwnerSessionId}", ownerSessionId);
        }
    }

    private string? GetActiveOwnerSessionId()
    {
        lock (_ownerLeaseGate)
        {
            return _activeOwnerSessionId;
        }
    }

    private void ResetRuntimeWarningEvidence(string reason)
    {
        ClearRuntimeWarningEvidence(reason);
    }

    private void ClearRuntimeWarningEvidence(string reason)
    {
        if (_runtimeWarning != RuntimeWarningEvidence.None)
        {
            _pipeServer.PushLogLine(
                "service",
                "Debug",
                $"runtime-warning diag: clear warning={_runtimeWarning}, strength={_runtimeWarningStrength}, weakCount={_weakConnectionEvidenceCount}, reason={reason}");
        }

        _runtimeWarning = RuntimeWarningEvidence.None;
        _runtimeWarningStrength = RuntimeWarningStrength.None;
        _weakConnectionEvidenceCount = 0;
    }

    private void ClearTunnelOwner(string reason)
    {
        string? ownerSessionId;
        lock (_ownerLeaseGate)
        {
            ownerSessionId = _activeOwnerSessionId;
            _activeOwnerSessionId = null;
            _activeOwnerHeartbeatUtc = null;
        }

        if (string.IsNullOrWhiteSpace(ownerSessionId))
        {
            return;
        }

        _logger.LogInformation(
            "Tunnel owner cleared for session {OwnerSessionId}; reason={Reason}",
            ownerSessionId,
            reason);
        _pipeServer.PushLogLine(
            "service",
            "Info",
            $"Tunnel owner cleared: session {ownerSessionId}, reason={reason}");
    }

    private RuntimeWarningTracker GetRuntimeWarningTracker() =>
        new(_runtimeWarning, _runtimeWarningStrength, _weakConnectionEvidenceCount);

    private void SetRuntimeWarningTracker(RuntimeWarningTracker tracker)
    {
        _runtimeWarning = tracker.Warning;
        _runtimeWarningStrength = tracker.Strength;
        _weakConnectionEvidenceCount = tracker.WeakConnectionEvidenceCount;
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(value => text.Contains(value, StringComparison.Ordinal));

    private TunnelLifecycleState ReadLifecycleState() => _lifecycleState;

    private void SetLifecycleState(TunnelLifecycleState lifecycleState) => _lifecycleState = lifecycleState;

    private async Task SafeStopSingBoxAsync()
    {
        try
        {
            await _singBoxManager.StopAsync(_stoppingToken);
        }
        catch
        {
            // Best-effort cleanup after start failure.
        }
    }

    internal static bool CanStartCapture(TunnelLifecycleState lifecycleState) =>
        lifecycleState == TunnelLifecycleState.Stopped;

    internal static bool CanStopCapture(TunnelLifecycleState lifecycleState) =>
        lifecycleState == TunnelLifecycleState.Running;

    internal static string GetStartBlockedReason(TunnelLifecycleState lifecycleState) => lifecycleState switch
    {
        TunnelLifecycleState.Starting => "Cannot start: tunnel startup is already in progress.",
        TunnelLifecycleState.Running => "Cannot start: tunnel is already running.",
        TunnelLifecycleState.Stopping => "Cannot start: tunnel shutdown is still in progress.",
        _ => "Cannot start: tunnel is not in a startable state."
    };

    internal static string GetStopBlockedReason(TunnelLifecycleState lifecycleState) => lifecycleState switch
    {
        TunnelLifecycleState.Stopped => "Cannot stop: tunnel is already stopped.",
        TunnelLifecycleState.Starting => "Cannot stop: tunnel startup is still in progress.",
        TunnelLifecycleState.Stopping => "Cannot stop: tunnel shutdown is already in progress.",
        _ => "Cannot stop: tunnel is not in a stoppable state."
    };

    private void AbortStart(string message)
    {
        _pipeServer.PushLogLine("service", "Error", message);
        SetLifecycleState(TunnelLifecycleState.Stopped);
        PushCurrentStatus();
    }
}

internal readonly record struct ServiceStatusSummary(
    TunnelStatusMode SelectedMode,
    bool CaptureRunning,
    TunnelLifecycleState LifecycleState,
    SingBoxStatus SingBoxStatus,
    bool SingBoxRunning,
    bool TunnelInterfaceUp,
    Guid? ActiveProfileId,
    string? ActiveProfileName,
    string? ActiveOwnerSessionId,
    int ProxyRuleCount,
    int DirectRuleCount,
    int BlockRuleCount,
    RuntimeWarningEvidence RuntimeWarning);

internal readonly record struct TunPolicySummary(
    string AppPath,
    RuleMode RuleMode,
    string MappedAction,
    string? MappedOutbound);

internal enum RuntimeWarningDetail
{
    None,
    AuthenticationFailure,
    StrongConnectionProblem,
    WeakConnectionNoise
}

internal enum RuntimeWarningStrength
{
    None = 0,
    Weak = 1,
    Strong = 2
}

internal readonly record struct RuntimeWarningTracker(
    RuntimeWarningEvidence Warning,
    RuntimeWarningStrength Strength,
    int WeakConnectionEvidenceCount)
{
    public static RuntimeWarningTracker Empty => new(
        RuntimeWarningEvidence.None,
        RuntimeWarningStrength.None,
        0);
}

internal readonly record struct TunnelOwnerLeaseState(
    string? OwnerSessionId,
    DateTimeOffset? LastHeartbeatUtc)
{
    public static TunnelOwnerLeaseState Empty => new(null, null);
}
