using System.IO;

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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static string DefaultConfigPath => Path.Combine(DefaultDataRoot, "config.json");

    public static string DefaultSingBoxDirectory => Path.Combine(DefaultDataRoot, "singbox");

    public static string DefaultLogsDirectory => Path.Combine(DefaultDataRoot, "logs");

    public static IReadOnlyList<string> GetDefaultServiceExecutableCandidates()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, Path.Combine(AppContext.BaseDirectory, ServiceExecutableName));

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        if (repoRoot is not null)
        {
            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "src",
                "TunnelFlow.Service",
                "bin",
                "Debug",
                "net8.0-windows",
                ServiceExecutableName));

            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "src",
                "TunnelFlow.Service",
                "bin",
                "Release",
                "net8.0-windows",
                ServiceExecutableName));
        }

        return candidates;
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
}
