namespace TunnelFlow.UI.Services;

public interface IServiceControlManager
{
    Task StartAsync(CancellationToken cancellationToken);

    Task RestartAsync(CancellationToken cancellationToken);
}

public sealed class ServiceNotInstalledException : InvalidOperationException
{
    public ServiceNotInstalledException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
