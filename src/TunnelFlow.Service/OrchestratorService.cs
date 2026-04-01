using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TunnelFlow.Capture.Policy;
using TunnelFlow.Capture.TcpRedirect;
using TunnelFlow.Core;
using TunnelFlow.Core.IPC.Responses;
using TunnelFlow.Core.Models;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;
using TunnelFlow.Service.Tun;

namespace TunnelFlow.Service;

public sealed class OrchestratorService : BackgroundService
{
    private readonly ISingBoxManager _singBoxManager;
    private readonly ICaptureEngine _captureEngine;
    private readonly IPolicyEngine _policyEngine;
    private readonly ITcpRedirectProvider _tcpRedirectProvider;
    private readonly ITunOrchestrator _tunOrchestrator;
    private readonly ISessionRegistry _sessionRegistry;
    private readonly ConfigStore _configStore;
    private readonly PipeServer _pipeServer;
    private readonly ILogger<OrchestratorService> _logger;

    private TunnelFlowConfig _config = new();
    private bool _captureRunning;
    private bool _legacyCaptureActive;
    private bool _tunModeActive;
    private int _singBoxRestartAttempts;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private CancellationToken _stoppingToken;

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TunnelFlow");

    public OrchestratorService(
        ISingBoxManager singBoxManager,
        ICaptureEngine captureEngine,
        IPolicyEngine policyEngine,
        ITcpRedirectProvider tcpRedirectProvider,
        ITunOrchestrator tunOrchestrator,
        ISessionRegistry sessionRegistry,
        ConfigStore configStore,
        PipeServer pipeServer,
        ILogger<OrchestratorService> logger)
    {
        _singBoxManager = singBoxManager;
        _captureEngine = captureEngine;
        _policyEngine = policyEngine;
        _tcpRedirectProvider = tcpRedirectProvider;
        _tunOrchestrator = tunOrchestrator;
        _sessionRegistry = sessionRegistry;
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

        if (_config.StartCaptureOnServiceStart && _config.ActiveProfileId is not null)
        {
            try { await StartCaptureAsync(); }
            catch (Exception ex) { _logger.LogError(ex, "Auto-start capture failed"); }
        }

        PushCurrentStatus();
        await pipeTask;
    }

    public async Task StartCaptureAsync()
    {
        await _captureLock.WaitAsync(_stoppingToken);
        try
        {
            if (_captureRunning)
            {
                _pipeServer.PushLogLine("service", "Warning", "Tunnel is already running.");
                return;
            }

            _pipeServer.PushLogLine("service", "Info", "Starting tunnel...");

            var config = await _configStore.LoadAsync();
            _config = config;

            if (config.ActiveProfileId is null)
            {
                _pipeServer.PushLogLine("service", "Error",
                    "Cannot start: no active VLESS profile selected.");
                return;
            }

            var profile = config.Profiles.FirstOrDefault(p => p.Id == config.ActiveProfileId);
            if (profile is null)
            {
                _pipeServer.PushLogLine("service", "Error",
                    "Cannot start: active profile not found in config.");
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.ServerAddress) ||
                string.IsNullOrWhiteSpace(profile.UserId))
            {
                _pipeServer.PushLogLine("service", "Error",
                    "Cannot start: profile is missing ServerAddress or UserId.");
                return;
            }

            // Resolve server IP for self-exclusion (non-negotiable per CURSOR_RULES.md §1)
            IPAddress[] serverAddresses;
            try
            {
                serverAddresses = await Dns.GetHostAddressesAsync(profile.ServerAddress);
            }
            catch (Exception ex)
            {
                _pipeServer.PushLogLine("service", "Error",
                    $"Cannot start: failed to resolve {profile.ServerAddress}: {ex.Message}");
                return;
            }

            _pipeServer.PushLogLine("service", "Info",
                $"Resolved {profile.ServerAddress} → {string.Join(", ", serverAddresses.Select(a => a.ToString()))}");

            Directory.CreateDirectory(DataDir);

            var singBoxExe = ResolveSingBoxPath();
            var tunModeSelection = TunModeSelector.Select(
                config.UseTunMode,
                _tunOrchestrator.SupportsActivation,
                _tunOrchestrator.ResolvedWintunPath);

            if (!File.Exists(singBoxExe))
            {
                _pipeServer.PushLogLine("service", "Error",
                    $"sing-box binary not found at: {singBoxExe}");
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

            if (tunModeSelection.UseTunModeRequested && tunModeSelection.SelectedMode != TunnelMode.Tun)
            {
                _pipeServer.PushLogLine(
                    "service",
                    "Warning",
                    $"TUN mode requested but legacy mode remains active: {tunModeSelection.SelectionReason}");
            }

            var logDir = Path.Combine(DataDir, "logs");
            Directory.CreateDirectory(logDir);

            if (tunModeSelection.SelectedMode == TunnelMode.Tun)
            {
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
                        "TUN activation failed; falling back to legacy mode wintunPath={WintunPath}",
                        tunModeSelection.WintunPath);
                    _pipeServer.PushLogLine(
                        "service",
                        "Warning",
                        $"TUN activation failed, falling back to legacy mode: {ex.Message}");
                    tunModeSelection = tunModeSelection with
                    {
                        SelectedMode = TunnelMode.Legacy,
                        SelectionReason = "tun-activation-failed"
                    };
                    _tunModeActive = false;
                }
            }

            var runtimePlan = BuildRuntimePlan(tunModeSelection.SelectedMode);
            _logger.LogInformation(
                "Tunnel runtime plan selectedMode={SelectedMode} legacyCaptureEnabled={LegacyCaptureEnabled} localRelayEnabled={LocalRelayEnabled} winpkFilterEnabled={WinpkFilterEnabled}",
                runtimePlan.SelectedMode,
                runtimePlan.LegacyCaptureEnabled,
                runtimePlan.LocalRelayEnabled,
                runtimePlan.WinpkFilterEnabled);
            _pipeServer.PushLogLine(
                "service",
                "Info",
                $"Runtime plan: selectedMode={runtimePlan.SelectedMode}, legacyCaptureEnabled={runtimePlan.LegacyCaptureEnabled}, localRelayEnabled={runtimePlan.LocalRelayEnabled}, winpkFilterEnabled={runtimePlan.WinpkFilterEnabled}");

            var singBoxConfig = new SingBoxConfig
            {
                SocksPort = config.SocksPort,
                UseTunMode = tunModeSelection.SelectedMode == TunnelMode.Tun,
                Rules = config.Rules,
                BinaryPath = singBoxExe,
                ConfigOutputPath = Path.Combine(DataDir, "singbox_last.json"),
                LogOutputPath = Path.Combine(logDir, "singbox.log"),
                RestartDelay = TimeSpan.FromSeconds(3),
                MaxRestartAttempts = 5
            };

            _pipeServer.PushLogLine("service", "Info", $"Starting sing-box: {singBoxExe}");
            await _singBoxManager.StartAsync(profile, singBoxConfig, _stoppingToken);

            // Self-exclusion: sing-box binary + this service binary (non-negotiable per CURSOR_RULES.md §1)
            var excludedPaths = new List<string> { singBoxExe };
            var servicePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(servicePath))
                excludedPaths.Add(servicePath);

            var captureConfig = new CaptureConfig
            {
                SocksPort = config.SocksPort,
                SocksAddress = IPAddress.Loopback,
                Rules = config.Rules,
                ExcludedProcessPaths = excludedPaths,
                ExcludedDestinations = serverAddresses.ToList()
            };

            _legacyCaptureActive = runtimePlan.LegacyCaptureEnabled;
            if (_legacyCaptureActive)
            {
                var redirectConfig = new WfpRedirectConfig
                {
                    UseWfpTcpRedirect = config.UseWfpTcpRedirect
                };
                _logger.LogInformation(
                    "TCP redirect feature state useWfpTcpRedirect={UseWfpTcpRedirect}",
                    redirectConfig.UseWfpTcpRedirect);
                await _tcpRedirectProvider.StartAsync(redirectConfig, _stoppingToken);

                await _captureEngine.StartAsync(captureConfig, _stoppingToken);
            }

            _captureRunning = true;
            _singBoxRestartAttempts = 0;
            _pipeServer.PushLogLine("service", "Info", "Tunnel started successfully.");
            PushCurrentStatus();
        }
        catch (Exception ex)
        {
            try
            {
                if (_tunModeActive)
                {
                    await _tunOrchestrator.StopAsync(_stoppingToken);
                    _tunModeActive = false;
                }
            }
            catch { }
            try { await _tcpRedirectProvider.StopAsync(_stoppingToken); } catch { }
            _legacyCaptureActive = false;
            _logger.LogError(ex, "StartCaptureAsync failed");
            _pipeServer.PushLogLine("service", "Error",
                $"StartCaptureAsync failed: {ex.GetType().Name}: {ex.Message}");
            _pipeServer.PushStatusChanged(captureRunning: false, SingBoxStatus.Stopped);
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task StopCaptureAsync()
    {
        await _captureLock.WaitAsync(_stoppingToken);
        try
        {
            if (!_captureRunning)
                return;

            _pipeServer.PushLogLine("service", "Info", "Stopping tunnel...");
            if (_legacyCaptureActive)
            {
                await _captureEngine.StopAsync(_stoppingToken);
                await _tcpRedirectProvider.StopAsync(_stoppingToken);
            }
            if (_tunModeActive)
            {
                await _tunOrchestrator.StopAsync(_stoppingToken);
                _tunModeActive = false;
            }
            await _singBoxManager.StopAsync(_stoppingToken);

            _captureRunning = false;
            _legacyCaptureActive = false;
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

    private static string ResolveSingBoxPath()
    {
        // In development: AppContext.BaseDirectory is bin/Debug/net8.0-windows/
        // Walk up to repo root and into third_party/singbox/
        var candidate = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "third_party", "singbox", "sing-box.exe"));
        if (File.Exists(candidate))
            return candidate;

        // In deployed/installed scenarios the binary may sit next to the service exe
        return Path.Combine(AppContext.BaseDirectory, "sing-box.exe");
    }

    internal static TunnelRuntimePlan BuildRuntimePlan(TunnelMode selectedMode) =>
        selectedMode == TunnelMode.Tun
            ? new TunnelRuntimePlan(
                selectedMode,
                LegacyCaptureEnabled: false,
                LocalRelayEnabled: false,
                WinpkFilterEnabled: false)
            : new TunnelRuntimePlan(
                selectedMode,
                LegacyCaptureEnabled: true,
                LocalRelayEnabled: true,
                WinpkFilterEnabled: true);
}

internal readonly record struct TunnelRuntimePlan(
    TunnelMode SelectedMode,
    bool LegacyCaptureEnabled,
    bool LocalRelayEnabled,
    bool WinpkFilterEnabled);
