using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.ServiceProcess;

namespace TunnelFlow.Bootstrapper;

internal static class Program
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly string ScExecutablePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "sc.exe");

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
        if (!TryResolveServiceExecutablePath(command, out var serviceExecutablePath, out var resolutionFailureMessage))
        {
            WriteError(resolutionFailureMessage);
            return BootstrapperExitCode.ServiceBinaryNotFound;
        }

        if (IsServiceInstalled())
        {
            WriteError($"{BootstrapperPaths.ServiceName} service is already installed.");
            return BootstrapperExitCode.AlreadyInstalled;
        }

        Console.WriteLine($"Installing {BootstrapperPaths.ServiceName} service.");
        Console.WriteLine($"Service executable: {serviceExecutablePath}");
        Console.WriteLine($"Startup mode: {BootstrapperPaths.ServiceStartMode}");

        var createResult = RunScCommand(
            $"create {BootstrapperPaths.ServiceName} binPath= {QuoteForSc(serviceExecutablePath)} start= {BootstrapperPaths.ServiceStartMode} DisplayName= {QuoteForSc(BootstrapperPaths.ServiceDisplayName)}");
        if (!createResult.IsSuccess)
        {
            return MapScFailure(
                createResult.ExitCode,
                $"Failed to create {BootstrapperPaths.ServiceName} service.",
                createResult.Output);
        }

        Console.WriteLine($"{BootstrapperPaths.ServiceName} service registered.");
        return ExecuteStartService();
    }

    private static BootstrapperExitCode ExecuteRepair(BootstrapperCommand command)
    {
        if (!TryResolveServiceExecutablePath(command, out var serviceExecutablePath, out var resolutionFailureMessage))
        {
            WriteError(resolutionFailureMessage);
            return BootstrapperExitCode.ServiceBinaryNotFound;
        }

        Console.WriteLine($"Repairing {BootstrapperPaths.ServiceName} service.");
        Console.WriteLine($"Expected service executable: {serviceExecutablePath}");
        Console.WriteLine($"Expected startup mode: {BootstrapperPaths.ServiceStartMode}");

        if (!IsServiceInstalled())
        {
            Console.WriteLine($"{BootstrapperPaths.ServiceName} service is missing. Creating it.");
            return ExecuteInstall(command);
        }

        var configResult = RunScCommand(
            $"config {BootstrapperPaths.ServiceName} binPath= {QuoteForSc(serviceExecutablePath)} start= {BootstrapperPaths.ServiceStartMode} DisplayName= {QuoteForSc(BootstrapperPaths.ServiceDisplayName)}");
        if (!configResult.IsSuccess)
        {
            return MapScFailure(
                configResult.ExitCode,
                $"Failed to refresh {BootstrapperPaths.ServiceName} service configuration.",
                configResult.Output);
        }

        Console.WriteLine($"{BootstrapperPaths.ServiceName} service configuration refreshed.");
        return ExecuteRestartService();
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

    private static bool IsServiceInstalled()
    {
        try
        {
            using var controller = CreateController();
            _ = controller.Status;
            return true;
        }
        catch (Exception ex) when (IsServiceMissing(ex))
        {
            return false;
        }
    }

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

    private static bool TryResolveServiceExecutablePath(
        BootstrapperCommand command,
        out string serviceExecutablePath,
        out string failureMessage)
    {
        if (!string.IsNullOrWhiteSpace(command.ServiceExecutablePath))
        {
            serviceExecutablePath = Path.GetFullPath(command.ServiceExecutablePath);
            if (File.Exists(serviceExecutablePath))
            {
                failureMessage = string.Empty;
                return true;
            }

            failureMessage =
                $"Service executable was not found at '{serviceExecutablePath}'. Provide a valid path with --service-exe <path>.";
            return false;
        }

        var candidates = BootstrapperPaths.GetDefaultServiceExecutableCandidates();
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                serviceExecutablePath = candidate;
                failureMessage = string.Empty;
                return true;
            }
        }

        serviceExecutablePath = string.Empty;
        failureMessage =
            "Service executable could not be resolved from default locations." +
            Environment.NewLine +
            "Checked candidate paths:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, candidates.Select(candidate => $"  - {candidate}")) +
            Environment.NewLine +
            "Use --service-exe <path> to specify TunnelFlow.Service.exe explicitly.";
        return false;
    }

    private static BootstrapperExitCode MapScFailure(int exitCode, string context, string output)
    {
        if (!string.IsNullOrWhiteSpace(output))
        {
            WriteError(output);
        }

        return exitCode switch
        {
            5 => WriteAndReturn(BootstrapperExitCode.AccessDenied, $"{context} Access denied."),
            1060 => WriteAndReturn(BootstrapperExitCode.NotInstalled, $"{context} Service is not installed."),
            1073 => WriteAndReturn(BootstrapperExitCode.AlreadyInstalled, $"{context} Service already exists."),
            _ => WriteAndReturn(BootstrapperExitCode.UnknownError, $"{context} sc.exe exited with code {exitCode}.")
        };
    }

    private static BootstrapperExitCode WriteAndReturn(BootstrapperExitCode code, string message)
    {
        WriteError(message);
        return code;
    }

    private static ScCommandResult RunScCommand(string arguments)
    {
        if (!File.Exists(ScExecutablePath))
        {
            throw new FileNotFoundException("sc.exe was not found.", ScExecutablePath);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ScExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        var outputBuilder = new StringBuilder();

        process.Start();
        outputBuilder.Append(process.StandardOutput.ReadToEnd());
        outputBuilder.Append(process.StandardError.ReadToEnd());
        process.WaitForExit();

        return new ScCommandResult(process.ExitCode, outputBuilder.ToString().Trim());
    }

    private static string QuoteForSc(string value) => $"\"{value}\"";

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

    private readonly record struct ScCommandResult(int ExitCode, string Output)
    {
        public bool IsSuccess => ExitCode == 0;
    }
}
