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

    public Task StartAsync(CancellationToken cancellationToken) =>
        Task.Run(() => StartCore(), cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken) =>
        Task.Run(() => RestartCore(), cancellationToken);

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
