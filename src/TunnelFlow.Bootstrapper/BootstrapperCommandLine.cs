namespace TunnelFlow.Bootstrapper;

internal enum BootstrapperVerb
{
    Install,
    Repair,
    Uninstall,
    StartService,
    RestartService
}

internal sealed record BootstrapperCommand(BootstrapperVerb Verb, string? ServiceExecutablePath);

internal static class BootstrapperCommandLine
{
    public static bool TryParse(string[] args, out BootstrapperCommand? command, out string? error)
    {
        command = null;
        error = null;

        if (args.Length == 0)
        {
            error = "A lifecycle verb is required.";
            return false;
        }

        if (!TryParseVerb(args[0], out var verb))
        {
            error = $"Unknown verb '{args[0]}'.";
            return false;
        }

        string? serviceExecutablePath = null;

        for (var index = 1; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--service-exe", StringComparison.OrdinalIgnoreCase))
            {
                error = $"Unknown argument '{args[index]}'.";
                return false;
            }

            if (index + 1 >= args.Length)
            {
                error = "Missing value for '--service-exe'.";
                return false;
            }

            serviceExecutablePath = args[index + 1];
            index++;
        }

        command = new BootstrapperCommand(verb, serviceExecutablePath);
        return true;
    }

    public static string GetUsage() =>
        """
        Usage:
          TunnelFlow.Bootstrapper.exe install [--service-exe <path>]
          TunnelFlow.Bootstrapper.exe repair [--service-exe <path>]
          TunnelFlow.Bootstrapper.exe uninstall
          TunnelFlow.Bootstrapper.exe start-service
          TunnelFlow.Bootstrapper.exe restart-service
        """;

    private static bool TryParseVerb(string value, out BootstrapperVerb verb)
    {
        switch (value.ToLowerInvariant())
        {
            case "install":
                verb = BootstrapperVerb.Install;
                return true;
            case "repair":
                verb = BootstrapperVerb.Repair;
                return true;
            case "uninstall":
                verb = BootstrapperVerb.Uninstall;
                return true;
            case "start-service":
                verb = BootstrapperVerb.StartService;
                return true;
            case "restart-service":
                verb = BootstrapperVerb.RestartService;
                return true;
            default:
                verb = default;
                return false;
        }
    }
}
