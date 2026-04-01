using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace TunnelFlow.Service.Tun;

public sealed class WintunTunOrchestrator : ITunOrchestrator
{
    private readonly ILogger<WintunTunOrchestrator> _logger;
    private readonly Func<string, nint> _loadLibrary;
    private readonly Action<nint> _freeLibrary;
    private nint _wintunHandle;

    public WintunTunOrchestrator(ILogger<WintunTunOrchestrator> logger)
        : this(logger, WintunPathResolver.Resolve(), NativeLibrary.Load, NativeLibrary.Free)
    {
    }

    public WintunTunOrchestrator(
        ILogger<WintunTunOrchestrator> logger,
        string resolvedWintunPath,
        Func<string, nint> loadLibrary,
        Action<nint> freeLibrary)
    {
        _logger = logger;
        ResolvedWintunPath = resolvedWintunPath;
        _loadLibrary = loadLibrary;
        _freeLibrary = freeLibrary;
    }

    public string ResolvedWintunPath { get; }

    public bool SupportsActivation => File.Exists(ResolvedWintunPath);

    public Task StartAsync(TunOrchestrationConfig config, CancellationToken cancellationToken)
    {
        if (!config.UseTunMode)
            return Task.CompletedTask;

        _logger.LogInformation(
            "TUN activation attempt wintunPath={WintunPath}",
            ResolvedWintunPath);

        if (!SupportsActivation)
            throw new InvalidOperationException($"wintun.dll not found at {ResolvedWintunPath}");

        if (_wintunHandle != 0)
        {
            _logger.LogInformation(
                "TUN activation already active wintunPath={WintunPath}",
                ResolvedWintunPath);
            return Task.CompletedTask;
        }

        _wintunHandle = _loadLibrary(ResolvedWintunPath);
        _logger.LogInformation(
            "TUN activation succeeded wintunPath={WintunPath}",
            ResolvedWintunPath);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_wintunHandle == 0)
            return Task.CompletedTask;

        _freeLibrary(_wintunHandle);
        _wintunHandle = 0;
        _logger.LogInformation(
            "TUN activation stopped wintunPath={WintunPath}",
            ResolvedWintunPath);
        return Task.CompletedTask;
    }
}
