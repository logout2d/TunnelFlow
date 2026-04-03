using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace TunnelFlow.UI.Services;

public sealed class WindowsServiceControlManager : IServiceControlManager
{
    private const string ServiceName = "TunnelFlow";
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);
    private static readonly string PowerShellPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        @"WindowsPowerShell\v1.0\powershell.exe");

    public Task InstallAsync(CancellationToken cancellationToken) =>
        InvokeBootstrapperAsync("install", cancellationToken);

    public Task RepairAsync(CancellationToken cancellationToken) =>
        InvokeBootstrapperAsync("repair", cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) =>
        InvokeBootstrapperOrFallbackAsync("start-service", StartCore, cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken) =>
        InvokeBootstrapperOrFallbackAsync("restart-service", RestartCore, cancellationToken);

    private static Task InvokeBootstrapperOrFallbackAsync(
        string verb,
        Action fallbackAction,
        CancellationToken cancellationToken)
    {
        var bootstrapperPath = ResolveBootstrapperExecutablePath();
        if (bootstrapperPath is not null)
        {
            return InvokeBootstrapperAsync(bootstrapperPath, verb, cancellationToken);
        }

        return Task.Run(fallbackAction, cancellationToken);
    }

    private static void StartCore()
    {
        using var controller = CreateController();

        try
        {
            controller.Refresh();
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                return;
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, OperationTimeout);
        }
        catch (Exception ex) when (RequiresElevation(ex))
        {
            RunElevatedCommand($"Start-Service -Name '{ServiceName}'");
        }
    }

    private static void RestartCore()
    {
        using var controller = CreateController();

        try
        {
            controller.Refresh();

            if (controller.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, OperationTimeout);
                controller.Refresh();
            }

            if (controller.Status is not ServiceControllerStatus.Running and not ServiceControllerStatus.StartPending)
            {
                controller.Start();
                controller.WaitForStatus(ServiceControllerStatus.Running, OperationTimeout);
            }
        }
        catch (Exception ex) when (RequiresElevation(ex))
        {
            RunElevatedCommand($"Restart-Service -Name '{ServiceName}' -Force");
        }
    }

    private static Task InvokeBootstrapperAsync(string verb, CancellationToken cancellationToken)
    {
        var bootstrapperPath = ResolveBootstrapperExecutablePath();
        if (bootstrapperPath is null)
        {
            throw new InvalidOperationException(
                "TunnelFlow.Bootstrapper.exe was not found. Build or deploy the bootstrapper, or use the existing service recovery path.");
        }

        return InvokeBootstrapperAsync(bootstrapperPath, verb, cancellationToken);
    }

    private static async Task InvokeBootstrapperAsync(
        string bootstrapperPath,
        string verb,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = bootstrapperPath,
                Arguments = verb,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(bootstrapperPath) ?? AppContext.BaseDirectory
            }) ?? throw new InvalidOperationException($"Failed to start bootstrapper verb '{verb}'.");

            await process.WaitForExitAsync(cancellationToken);
            EnsureBootstrapperSucceeded(verb, process.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Service action was canceled by the user.", ex);
        }
    }

    private static void EnsureBootstrapperSucceeded(string verb, int exitCode)
    {
        switch (exitCode)
        {
            case 0:
                return;
            case 4 when string.Equals(verb, "install", StringComparison.OrdinalIgnoreCase):
                return;
            case 5:
                throw new ServiceNotInstalledException("TunnelFlow service is not installed.");
            case 3:
                throw new InvalidOperationException("TunnelFlow.Service.exe could not be resolved for the bootstrapper command.");
            case 7:
                throw new InvalidOperationException("Service action was canceled by the user.");
            case 8:
                throw new InvalidOperationException("Service action requires administrator approval.");
            case 9:
                throw new InvalidOperationException($"Bootstrapper verb '{verb}' is not implemented.");
            default:
                throw new InvalidOperationException(
                    $"Bootstrapper command '{verb}' failed with exit code {exitCode}.");
        }
    }

    private static ServiceController CreateController()
    {
        try
        {
            return new ServiceController(ServiceName);
        }
        catch (InvalidOperationException ex)
        {
            throw new ServiceNotInstalledException("TunnelFlow service is not installed.", ex);
        }
    }

    private static string? ResolveBootstrapperExecutablePath()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, Path.Combine(AppContext.BaseDirectory, "TunnelFlow.Bootstrapper.exe"));

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is not null)
        {
            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "src",
                "TunnelFlow.Bootstrapper",
                "bin",
                "Debug",
                "net8.0-windows",
                "TunnelFlow.Bootstrapper.exe"));

            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "src",
                "TunnelFlow.Bootstrapper",
                "bin",
                "Release",
                "net8.0-windows",
                "TunnelFlow.Bootstrapper.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindRepositoryRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TunnelFlow.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(fullPath);
        }
    }

    private static bool RequiresElevation(Exception exception)
    {
        return FindWin32Exception(exception)?.NativeErrorCode == 5;
    }

    private static Win32Exception? FindWin32Exception(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is Win32Exception win32)
            {
                return win32;
            }

            current = current.InnerException;
        }

        return null;
    }

    private static void RunElevatedCommand(string command)
    {
        if (!File.Exists(PowerShellPath))
        {
            throw new InvalidOperationException("Windows PowerShell was not found.");
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = PowerShellPath,
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Failed to start elevated service command.");

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Service control command failed with exit code {process.ExitCode}.");
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Service action was canceled by the user.", ex);
        }
    }
}
