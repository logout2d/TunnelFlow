using System.IO;

namespace TunnelFlow.Bootstrapper;

internal static class BootstrapperPaths
{
    public const string ProductName = "TunnelFlow";
    public const string ServiceName = "TunnelFlow";
    public const string ServiceExecutableName = "TunnelFlow.Service.exe";
    public const string BootstrapperExecutableName = "TunnelFlow.Bootstrapper.exe";
    public const string UiExecutableName = "TunnelFlow.UI.exe";

    public static string DefaultInstallRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static string DefaultConfigPath => Path.Combine(DefaultDataRoot, "config.json");

    public static string DefaultSingBoxDirectory => Path.Combine(DefaultDataRoot, "singbox");

    public static string DefaultLogsDirectory => Path.Combine(DefaultDataRoot, "logs");

    public static string ResolveDefaultServiceExecutablePath() =>
        Path.Combine(AppContext.BaseDirectory, ServiceExecutableName);
}
