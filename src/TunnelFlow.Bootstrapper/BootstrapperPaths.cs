using System.IO;
using TunnelFlow.Core;

namespace TunnelFlow.Bootstrapper;

internal static class BootstrapperPaths
{
    public const string ProductName = "TunnelFlow";
    public const string ServiceName = "TunnelFlow";
    public const string ServiceDisplayName = "TunnelFlow";
    public const string ServiceExecutableName = "TunnelFlow.Service.exe";
    public const string BootstrapperExecutableName = "TunnelFlow.Bootstrapper.exe";
    public const string UiExecutableName = "TunnelFlow.UI.exe";
    public const string ServiceStartMode = "auto";

    public static string DefaultInstallRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    public static string DefaultDataRoot =>
        RuntimePaths.Current.RuntimeRoot;

    public static string DefaultConfigPath => RuntimePaths.Current.CurrentConfigPath;

    public static string DefaultSingBoxDirectory => Path.Combine(DefaultDataRoot, "singbox");

    public static string DefaultLogsDirectory => RuntimePaths.Current.CurrentLogsRoot;

    public static IReadOnlyList<string> GetDefaultServiceExecutableCandidates() =>
        RuntimePaths.Current.GetServiceExecutableCandidates();
}
