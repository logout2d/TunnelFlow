using System.ComponentModel;
using System.ServiceProcess;

namespace TunnelFlow.Bootstrapper;

internal static class Program
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private static int Main(string[] args)
    {
        var exitCode = Run(args);
        return (int)exitCode;
    }

    internal static BootstrapperExitCode Run(string[] args)
    {
        if (!BootstrapperCommandLine.TryParse(args, out var command, out var error))
        {
            WriteError(error ?? "Invalid arguments.");
            Console.WriteLine(BootstrapperCommandLine.GetUsage());
            return BootstrapperExitCode.InvalidArguments;
        }

        try
        {
            return command!.Verb switch
            {
                BootstrapperVerb.Install => ExecuteInstall(command),
                BootstrapperVerb.Repair => ExecuteRepair(command),
                BootstrapperVerb.Uninstall => ExecuteUninstall(),
                BootstrapperVerb.StartService => ExecuteStartService(),
                BootstrapperVerb.RestartService => ExecuteRestartService(),
                _ => BootstrapperExitCode.InvalidArguments
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            WriteError("The operation was canceled by the user.");
            return BootstrapperExitCode.UserCanceled;
        }
        catch (Exception ex)
        {
            WriteError($"Unexpected bootstrapper failure: {ex.Message}");
            return BootstrapperExitCode.UnknownError;
        }
    }

    private static BootstrapperExitCode ExecuteInstall(BootstrapperCommand command)
    {
        var serviceExecutablePath = ResolveServiceExecutablePath(command);
        if (!File.Exists(serviceExecutablePath))
        {
            WriteError($"Service executable was not found at '{serviceExecutablePath}'.");
            return BootstrapperExitCode.ServiceBinaryNotFound;
        }

        Console.WriteLine("Install is scaffolded in this phase.");
        Console.WriteLine($"Service name: {BootstrapperPaths.ServiceName}");
        Console.WriteLine($"Service executable: {serviceExecutablePath}");
        Console.WriteLine($"Recommended install root: {BootstrapperPaths.DefaultInstallRoot}");
        Console.WriteLine($"Recommended data root: {BootstrapperPaths.DefaultDataRoot}");
        return BootstrapperExitCode.NotImplemented;
    }

    private static BootstrapperExitCode ExecuteRepair(BootstrapperCommand command)
    {
        var serviceExecutablePath = ResolveServiceExecutablePath(command);
        if (!File.Exists(serviceExecutablePath))
        {
            WriteError($"Service executable was not found at '{serviceExecutablePath}'.");
            return BootstrapperExitCode.ServiceBinaryNotFound;
        }

        Console.WriteLine("Repair is scaffolded in this phase.");
        Console.WriteLine($"Service name: {BootstrapperPaths.ServiceName}");
        Console.WriteLine($"Service executable: {serviceExecutablePath}");
        Console.WriteLine($"Recommended install root: {BootstrapperPaths.DefaultInstallRoot}");
        Console.WriteLine($"Recommended data root: {BootstrapperPaths.DefaultDataRoot}");
        return BootstrapperExitCode.NotImplemented;
    }

    private static BootstrapperExitCode ExecuteUninstall()
    {
        Console.WriteLine("Uninstall is scaffolded in this phase.");
        Console.WriteLine($"Service name: {BootstrapperPaths.ServiceName}");
        Console.WriteLine($"Recommended install root: {BootstrapperPaths.DefaultInstallRoot}");
        Console.WriteLine($"Recommended data root: {BootstrapperPaths.DefaultDataRoot}");
        return BootstrapperExitCode.NotImplemented;
    }

    private static BootstrapperExitCode ExecuteStartService()
    {
        using var controller = CreateController();

        try
        {
            controller.Refresh();
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                Console.WriteLine($"{BootstrapperPaths.ServiceName} service is already running.");
                return BootstrapperExitCode.Success;
            }

            controller.Start();
            if (!WaitForStatus(controller, ServiceControllerStatus.Running))
            {
                WriteError($"{BootstrapperPaths.ServiceName} service did not reach Running state in time.");
                return BootstrapperExitCode.Timeout;
            }

            Console.WriteLine($"{BootstrapperPaths.ServiceName} service started.");
            return BootstrapperExitCode.Success;
        }
        catch (Exception ex) when (IsServiceMissing(ex))
        {
            WriteError($"{BootstrapperPaths.ServiceName} service is not installed.");
            return BootstrapperExitCode.NotInstalled;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            WriteError($"Access denied while starting {BootstrapperPaths.ServiceName} service.");
            return BootstrapperExitCode.AccessDenied;
        }
    }

    private static BootstrapperExitCode ExecuteRestartService()
    {
        using var controller = CreateController();

        try
        {
            controller.Refresh();

            if (controller.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
            {
                controller.Stop();
                if (!WaitForStatus(controller, ServiceControllerStatus.Stopped))
                {
                    WriteError($"{BootstrapperPaths.ServiceName} service did not stop in time.");
                    return BootstrapperExitCode.Timeout;
                }
            }

            controller.Refresh();
            if (controller.Status is not ServiceControllerStatus.Running and not ServiceControllerStatus.StartPending)
            {
                controller.Start();
                if (!WaitForStatus(controller, ServiceControllerStatus.Running))
                {
                    WriteError($"{BootstrapperPaths.ServiceName} service did not restart in time.");
                    return BootstrapperExitCode.Timeout;
                }
            }

            Console.WriteLine($"{BootstrapperPaths.ServiceName} service restarted.");
            return BootstrapperExitCode.Success;
        }
        catch (Exception ex) when (IsServiceMissing(ex))
        {
            WriteError($"{BootstrapperPaths.ServiceName} service is not installed.");
            return BootstrapperExitCode.NotInstalled;
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            WriteError($"Access denied while restarting {BootstrapperPaths.ServiceName} service.");
            return BootstrapperExitCode.AccessDenied;
        }
    }

    private static ServiceController CreateController() => new(BootstrapperPaths.ServiceName);

    private static bool WaitForStatus(ServiceController controller, ServiceControllerStatus targetStatus)
    {
        var deadline = DateTime.UtcNow + OperationTimeout;

        while (DateTime.UtcNow < deadline)
        {
            controller.Refresh();
            if (controller.Status == targetStatus)
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        controller.Refresh();
        return controller.Status == targetStatus;
    }

    private static string ResolveServiceExecutablePath(BootstrapperCommand command) =>
        string.IsNullOrWhiteSpace(command.ServiceExecutablePath)
            ? BootstrapperPaths.ResolveDefaultServiceExecutablePath()
            : Path.GetFullPath(command.ServiceExecutablePath);

    private static bool IsServiceMissing(Exception exception) =>
        FindWin32Exception(exception)?.NativeErrorCode == 1060;

    private static bool IsAccessDenied(Exception exception) =>
        FindWin32Exception(exception)?.NativeErrorCode == 5;

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

    private static void WriteError(string message) => Console.Error.WriteLine(message);
}
