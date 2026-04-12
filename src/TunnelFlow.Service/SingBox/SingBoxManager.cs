using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.SingBox;

public sealed class SingBoxManager : ISingBoxManager
{
    internal static readonly TimeSpan TunStartupObservationWindow = TimeSpan.FromSeconds(8);
    internal static readonly TimeSpan TunStartupPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly SingBoxConfigBuilder _configBuilder;
    private readonly ILogger<SingBoxManager> _logger;
    private readonly object _stateGate = new();

    private Process? _process;
    private CancellationTokenSource _watchdogCts = new();
    private SingBoxStatus _status = SingBoxStatus.Stopped;
    private SingBoxStartupTracker? _startupTracker;

    private VlessProfile? _lastProfile;
    private SingBoxConfig? _lastConfig;

    public event EventHandler<SingBoxStatus>? StatusChanged;
    public event EventHandler<string>? LogLine;

    public SingBoxManager(SingBoxConfigBuilder configBuilder, ILogger<SingBoxManager> logger)
    {
        _configBuilder = configBuilder;
        _logger = logger;
    }

    public async Task StartAsync(VlessProfile profile, SingBoxConfig config, CancellationToken ct)
    {
        ResetWatchdogIfNeeded();

        _lastProfile = profile;
        _lastConfig = config;

        SetStatus(SingBoxStatus.Starting);

        // Kill any leftover sing-box processes from a previous (unclean) run before
        // starting a fresh one; otherwise stale sing-box resources can linger.
        KillOrphanedSingBoxProcesses(config.BinaryPath);
        await Task.Delay(500, ct);

        var configJson = _configBuilder.Build(profile, config);
        var configDir = Path.GetDirectoryName(config.ConfigOutputPath);
        if (configDir is not null)
            Directory.CreateDirectory(configDir);

        await File.WriteAllTextAsync(config.ConfigOutputPath, configJson, ct);
        await EnsureCleanLogOutputFileAsync(config.LogOutputPath, ct);

        var startupTracker = new SingBoxStartupTracker();
        SetStartupTracker(startupTracker);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = config.BinaryPath,
                Arguments = $"run -c \"{config.ConfigOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _process.OutputDataReceived += (_, e) => ForwardLog(e.Data);
        _process.ErrorDataReceived += (_, e) => ForwardLog(e.Data);
        _process.Exited += OnProcessExited;

        try
        {
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
        catch
        {
            ClearStartupTracker(startupTracker);
            CleanupProcess();
            throw;
        }

        _logger.LogInformation("sing-box started (pid {Pid})", _process.Id);
        _logger.LogInformation(
            "sing-box startup readiness mode=tun strategy=process-observation-plus-startup-fatal-log-detection startupWindowMs={StartupWindowMs} pollIntervalMs={PollIntervalMs}",
            TunStartupObservationWindow.TotalMilliseconds,
            TunStartupPollInterval.TotalMilliseconds);

        var readiness = await WaitForTunStartupReadinessAsync(
            () => _process is null || _process.HasExited,
            startupTracker.GetFailure,
            TunStartupObservationWindow,
            TunStartupPollInterval,
            ct);

        if (!readiness.Ready)
        {
            _logger.LogError(
                "sing-box readiness failed strategy=process-observation-plus-startup-fatal-log-detection reason={Reason} exitCode={ExitCode}",
                readiness.Reason,
                TryGetExitCode(_process));
            throw new InvalidOperationException(
                $"sing-box startup readiness failed in TUN mode: {readiness.Reason}");
        }

        ClearStartupTracker(startupTracker);
        _logger.LogInformation(
            "sing-box readiness succeeded strategy=process-observation-plus-startup-fatal-log-detection reason={Reason}",
            readiness.Reason);

        SetStatus(SingBoxStatus.Running);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _watchdogCts.Cancel();
        SetStartupTracker(null);

        if (_process is { HasExited: false })
        {
            try
            {
                // Force-kill with entire process tree so child processes (if any) are also gone.
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(ct)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch { }
        }

        CleanupProcess();
        SetStatus(SingBoxStatus.Stopped);
    }

    public async Task RestartAsync(CancellationToken ct)
    {
        await StopAsync(ct);
        _watchdogCts = new CancellationTokenSource();

        if (_lastProfile is not null && _lastConfig is not null)
            await StartAsync(_lastProfile, _lastConfig, ct);
    }

    public SingBoxStatus GetStatus() => _status;

    public void Dispose()
    {
        _watchdogCts.Cancel();
        CleanupProcess();
        _watchdogCts.Dispose();
    }

    private void SetStatus(SingBoxStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    private void ForwardLog(string? line)
    {
        if (line is null) return;

        if (TryMatchTunStartupFatalLine(line, out var matchedPattern))
        {
            var startupTracker = GetStartupTracker();
            if (startupTracker?.TryFail("startup-fatal-tun-log-line") == true)
            {
                _logger.LogWarning(
                    "sing-box detected startup-fatal TUN log line matchedPattern={MatchedPattern} line={Line}",
                    matchedPattern,
                    line);
            }
        }

        _logger.LogDebug("[sing-box] {Line}", line);
        LogLine?.Invoke(this, line);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _ = HandleExitAsync();
    }

    private Task HandleExitAsync()
    {
        if (_watchdogCts.IsCancellationRequested)
            return Task.CompletedTask;

        var exitCode = TryGetExitCode(_process);
        var startupTracker = GetStartupTracker();
        if (startupTracker is not null)
        {
            if (startupTracker.TryFail("process-exited-during-startup-window"))
            {
                _logger.LogError(
                    "sing-box exited unexpectedly during TUN startup exitCode={ExitCode}; startup readiness will fail and cleanup will be required",
                    exitCode);
            }

            return Task.CompletedTask;
        }

        _logger.LogError(
            "sing-box exited unexpectedly after TUN startup stabilized exitCode={ExitCode}; auto-restart is disabled so the orchestrator can fail closed and clean up TUN state",
            exitCode);
        SetStatus(SingBoxStatus.Crashed);
        return Task.CompletedTask;
    }

    private void CleanupProcess()
    {
        if (_process is null) return;
        _process.Exited -= OnProcessExited;
        try { _process.Dispose(); } catch { }
        _process = null;
    }

    private static void KillOrphanedSingBoxProcesses(string binaryPath)
    {
        try
        {
            var exeName = Path.GetFileNameWithoutExtension(binaryPath);
            var processes = Process.GetProcessesByName(exeName);
            foreach (var p in processes)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(2000);
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
    }

    internal static async Task EnsureCleanLogOutputFileAsync(string? logOutputPath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(logOutputPath))
            return;

        var logDir = Path.GetDirectoryName(logOutputPath);
        if (!string.IsNullOrWhiteSpace(logDir))
            Directory.CreateDirectory(logDir);

        await File.WriteAllTextAsync(logOutputPath, string.Empty, ct);
    }

    internal static async Task<SingBoxReadinessResult> WaitForTunStartupReadinessAsync(
        Func<bool> hasExited,
        Func<SingBoxReadinessResult?> getStartupFailure,
        TimeSpan observationWindow,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        if (hasExited())
            return new SingBoxReadinessResult(false, "process-already-exited");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(observationWindow);

        while (!cts.Token.IsCancellationRequested)
        {
            var startupFailure = getStartupFailure();
            if (startupFailure is not null)
                return startupFailure.Value;

            try
            {
                await Task.Delay(pollInterval, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }

            if (hasExited())
                return new SingBoxReadinessResult(false, "process-exited-during-startup-window");

            startupFailure = getStartupFailure();
            if (startupFailure is not null)
                return startupFailure.Value;
        }

        return new SingBoxReadinessResult(true, "startup-window-passed-without-fatal-tun-signals");
    }

    internal static bool TryMatchTunStartupFatalLine(string line, out string matchedPattern)
    {
        matchedPattern = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        var normalized = line.ToLowerInvariant();
        if (normalized.Contains("fatal", StringComparison.Ordinal))
        {
            matchedPattern = "FATAL";
            return true;
        }

        if (normalized.Contains("open interface take too much time to finish", StringComparison.Ordinal))
        {
            matchedPattern = "open interface take too much time to finish";
            return true;
        }

        if (normalized.Contains("configure tun interface", StringComparison.Ordinal) ||
            normalized.Contains("configurate tun interface", StringComparison.Ordinal))
        {
            matchedPattern = "configure tun interface";
            return true;
        }

        if (normalized.Contains("cannot create a file when that file already exist", StringComparison.Ordinal))
        {
            matchedPattern = "Cannot create a file when that file already exist";
            return true;
        }

        return false;
    }

    private static int? TryGetExitCode(Process? process)
    {
        if (process is null)
            return null;

        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch
        {
            return null;
        }
    }

    private void ResetWatchdogIfNeeded()
    {
        if (!_watchdogCts.IsCancellationRequested)
            return;

        _watchdogCts.Dispose();
        _watchdogCts = new CancellationTokenSource();
    }

    private SingBoxStartupTracker? GetStartupTracker()
    {
        lock (_stateGate)
        {
            return _startupTracker;
        }
    }

    private void SetStartupTracker(SingBoxStartupTracker? startupTracker)
    {
        lock (_stateGate)
        {
            _startupTracker = startupTracker;
        }
    }

    private void ClearStartupTracker(SingBoxStartupTracker startupTracker)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(_startupTracker, startupTracker))
            {
                _startupTracker = null;
            }
        }
    }
}

internal readonly record struct SingBoxReadinessResult(bool Ready, string Reason);

internal sealed class SingBoxStartupTracker
{
    private readonly object _gate = new();
    private SingBoxReadinessResult? _failure;

    public SingBoxReadinessResult? GetFailure()
    {
        lock (_gate)
        {
            return _failure;
        }
    }

    public bool TryFail(string reason)
    {
        lock (_gate)
        {
            if (_failure is not null)
                return false;

            _failure = new SingBoxReadinessResult(false, reason);
            return true;
        }
    }
}
