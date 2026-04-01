using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TunnelFlow.Core;
using TunnelFlow.Core.Models;

namespace TunnelFlow.Service.SingBox;

public sealed class SingBoxManager : ISingBoxManager
{
    internal static readonly TimeSpan TunStartupObservationWindow = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan TunStartupPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly SingBoxConfigBuilder _configBuilder;
    private readonly ILogger<SingBoxManager> _logger;

    private Process? _process;
    private CancellationTokenSource _watchdogCts = new();
    private int _restartAttempts;
    private SingBoxStatus _status = SingBoxStatus.Stopped;

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
        _lastProfile = profile;
        _lastConfig = config;

        SetStatus(SingBoxStatus.Starting);

        // Kill any leftover sing-box processes from a previous (unclean) run before
        // starting a fresh one; otherwise the SOCKS port stays in use.
        KillOrphanedSingBoxProcesses(config.BinaryPath);
        await Task.Delay(500, ct);

        var configJson = _configBuilder.Build(profile, config);
        var configDir = Path.GetDirectoryName(config.ConfigOutputPath);
        if (configDir is not null)
            Directory.CreateDirectory(configDir);

        await File.WriteAllTextAsync(config.ConfigOutputPath, configJson, ct);
        await EnsureCleanLogOutputFileAsync(config.LogOutputPath, ct);

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

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        _logger.LogInformation("sing-box started (pid {Pid})", _process.Id);
        var readinessStrategy = SelectReadinessStrategy(config);
        _logger.LogInformation(
            "sing-box startup readiness mode={Mode} strategy={Strategy}",
            config.UseTunMode ? "tun" : "legacy",
            readinessStrategy);

        switch (readinessStrategy)
        {
            case SingBoxReadinessStrategy.SocksPort:
            {
                _logger.LogInformation(
                    "sing-box readiness strategy=socks-port port={Port}",
                    config.SocksPort);

                bool responsive = await WaitForSocksPortAsync(config.SocksPort, ct);
                if (responsive)
                {
                    _logger.LogInformation(
                        "sing-box readiness succeeded strategy=socks-port reason=socks-port-responsive port={Port}",
                        config.SocksPort);
                }
                else
                {
                    _logger.LogWarning(
                        "sing-box readiness not confirmed strategy=socks-port reason=socks-port-not-responsive port={Port}",
                        config.SocksPort);
                }

                break;
            }

            case SingBoxReadinessStrategy.ProcessObservation:
            {
                _logger.LogInformation(
                    "sing-box readiness strategy=process-observation observationWindowMs={ObservationWindowMs}",
                    TunStartupObservationWindow.TotalMilliseconds);

                var readiness = await WaitForProcessObservationAsync(
                    () => _process is null || _process.HasExited,
                    TunStartupObservationWindow,
                    TunStartupPollInterval,
                    ct);

                if (!readiness.Ready)
                {
                    _logger.LogError(
                        "sing-box readiness failed strategy=process-observation reason={Reason} exitCode={ExitCode}",
                        readiness.Reason,
                        TryGetExitCode(_process));
                    throw new InvalidOperationException(
                        $"sing-box startup readiness failed in TUN mode: {readiness.Reason}");
                }

                _logger.LogInformation(
                    "sing-box readiness succeeded strategy=process-observation reason={Reason}",
                    readiness.Reason);
                break;
            }
        }

        SetStatus(SingBoxStatus.Running);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _watchdogCts.Cancel();

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
        _restartAttempts = 0;
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
        _logger.LogDebug("[sing-box] {Line}", line);
        LogLine?.Invoke(this, line);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _ = HandleExitAsync();
    }

    private async Task HandleExitAsync()
    {
        if (_watchdogCts.IsCancellationRequested)
            return;

        var config = _lastConfig;
        if (config is null) return;

        if (_restartAttempts >= config.MaxRestartAttempts)
        {
            _logger.LogCritical(
                "sing-box exceeded max restart attempts ({Max}). Fail-closed.",
                config.MaxRestartAttempts);
            SetStatus(SingBoxStatus.Crashed);
            return;
        }

        _restartAttempts++;
        _logger.LogWarning(
            "sing-box exited unexpectedly. Restart attempt {Attempt}/{Max} in {Delay} s",
            _restartAttempts, config.MaxRestartAttempts, config.RestartDelay.TotalSeconds);

        SetStatus(SingBoxStatus.Restarting);

        await Task.Delay(config.RestartDelay);

        if (_watchdogCts.IsCancellationRequested) return;

        try
        {
            await StartAsync(_lastProfile!, config, _watchdogCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart sing-box");
            SetStatus(SingBoxStatus.Crashed);
        }
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

    private static async Task<bool> WaitForSocksPortAsync(int port, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", port, cts.Token);
                return true;
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                await Task.Delay(300, cts.Token).ConfigureAwait(false);
            }
        }

        return false;
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

    internal static SingBoxReadinessStrategy SelectReadinessStrategy(SingBoxConfig config) =>
        config.UseTunMode
            ? SingBoxReadinessStrategy.ProcessObservation
            : SingBoxReadinessStrategy.SocksPort;

    internal static async Task<SingBoxReadinessResult> WaitForProcessObservationAsync(
        Func<bool> hasExited,
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
        }

        return new SingBoxReadinessResult(true, "process-stable-during-startup-window");
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
}

internal enum SingBoxReadinessStrategy
{
    SocksPort,
    ProcessObservation
}

internal readonly record struct SingBoxReadinessResult(bool Ready, string Reason);
