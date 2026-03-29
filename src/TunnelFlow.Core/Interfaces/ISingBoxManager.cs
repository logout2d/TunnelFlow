using TunnelFlow.Core.Models;

namespace TunnelFlow.Core;

/// <summary>
/// Manages the sing-box child process lifecycle, restart policy, and status notifications for the outbound VLESS hop.
/// </summary>
public interface ISingBoxManager : IDisposable
{
    Task StartAsync(VlessProfile profile, SingBoxConfig config, CancellationToken ct);

    Task StopAsync(CancellationToken ct);

    Task RestartAsync(CancellationToken ct);

    SingBoxStatus GetStatus();

    event EventHandler<SingBoxStatus>? StatusChanged;

    event EventHandler<string>? LogLine;
}
