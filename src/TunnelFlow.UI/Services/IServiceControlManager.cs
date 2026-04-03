namespace TunnelFlow.UI.Services;

public interface IServiceControlManager
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken);

    Task InstallAsync(CancellationToken cancellationToken);

    Task RepairAsync(CancellationToken cancellationToken);

    Task UninstallAsync(CancellationToken cancellationToken);

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

public sealed class ServiceControlAccessDeniedException : InvalidOperationException
{
    public ServiceControlAccessDeniedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ServiceControlTimeoutException : TimeoutException
{
    public ServiceControlTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class ServiceBootstrapperMissingException : InvalidOperationException
{
    public ServiceBootstrapperMissingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
