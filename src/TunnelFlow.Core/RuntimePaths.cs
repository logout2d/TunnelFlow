namespace TunnelFlow.Core;

public sealed class RuntimePaths
{
    public const string ProductName = "TunnelFlow";
    public const string ConfigFileName = "config.json";
    public const string SingBoxConfigFileName = "singbox_last.json";
    public const string ServiceLogFileName = "service.log";
    public const string SingBoxLogFileName = "singbox.log";
    public const string UiLogFileName = "ui.log";
    public const string ServiceExecutableName = "TunnelFlow.Service.exe";
    public const string BootstrapperExecutableName = "TunnelFlow.Bootstrapper.exe";
    public const string SingBoxExecutableName = "sing-box.exe";
    public const string WintunDllName = "wintun.dll";

    private RuntimePaths(string baseDirectory, string legacyDataRoot)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        RuntimeRoot = ResolveRuntimeRoot(BaseDirectory);
        LegacyDataRoot = Path.GetFullPath(legacyDataRoot);
    }

    public string BaseDirectory { get; }

    public string RuntimeRoot { get; }

    public string LegacyDataRoot { get; }

    public string ConfigRoot => Path.Combine(RuntimeRoot, "config");

    public string CurrentConfigPath => Path.Combine(ConfigRoot, ConfigFileName);

    public string LegacyConfigPath => Path.Combine(LegacyDataRoot, ConfigFileName);

    public string CurrentLogsRoot => Path.Combine(RuntimeRoot, "logs");

    public string LegacyLogsRoot => Path.Combine(LegacyDataRoot, "logs");

    public string ServiceLogPath => Path.Combine(CurrentLogsRoot, ServiceLogFileName);

    public string SingBoxLogPath => Path.Combine(CurrentLogsRoot, SingBoxLogFileName);

    public string SingBoxConfigPath => Path.Combine(ConfigRoot, SingBoxConfigFileName);

    public string UiLogsRoot => CurrentLogsRoot;

    public string UiLogPath => Path.Combine(UiLogsRoot, UiLogFileName);

    public string AppSettingsPath => Path.Combine(ConfigRoot, "appsettings.json");

    public string SystemRoot => Path.Combine(RuntimeRoot, "system");

    public string ServiceExecutablePath => Path.Combine(SystemRoot, ServiceExecutableName);

    public string BootstrapperExecutablePath => Path.Combine(SystemRoot, BootstrapperExecutableName);

    public string CoreRoot => Path.Combine(RuntimeRoot, "core");

    public string SingBoxExecutablePath => Path.Combine(CoreRoot, SingBoxExecutableName);

    public string WintunDllPath => Path.Combine(CoreRoot, WintunDllName);

    public string FlatServiceExecutablePath => Path.Combine(BaseDirectory, ServiceExecutableName);

    public string FlatBootstrapperExecutablePath => Path.Combine(BaseDirectory, BootstrapperExecutableName);

    public string FlatSingBoxExecutablePath => Path.Combine(BaseDirectory, SingBoxExecutableName);

    public string FlatWintunDllPath => Path.Combine(BaseDirectory, WintunDllName);

    public static string DefaultLegacyDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static RuntimePaths Current => Create();

    public static RuntimePaths Create(string? baseDirectory = null, string? legacyDataRoot = null) =>
        new(baseDirectory ?? AppContext.BaseDirectory, legacyDataRoot ?? DefaultLegacyDataRoot);

    public IReadOnlyList<string> GetServiceExecutableCandidates()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, ServiceExecutablePath);
        AddCandidate(candidates, FlatServiceExecutablePath);

        AddRepoBuildCandidates(candidates, "TunnelFlow.Service", ServiceExecutableName);
        return candidates;
    }

    public IReadOnlyList<string> GetBootstrapperExecutableCandidates()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, BootstrapperExecutablePath);
        AddCandidate(candidates, FlatBootstrapperExecutablePath);

        AddRepoBuildCandidates(candidates, "TunnelFlow.Bootstrapper", BootstrapperExecutableName);
        return candidates;
    }

    public IReadOnlyList<string> GetSingBoxExecutableCandidates()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, SingBoxExecutablePath);
        AddCandidate(candidates, FlatSingBoxExecutablePath);

        var repoRoot = FindRepositoryRoot(RuntimeRoot);
        if (repoRoot is not null)
        {
            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "third_party",
                "singbox",
                SingBoxExecutableName));
        }

        return candidates;
    }

    public IReadOnlyList<string> GetWintunDllCandidates()
    {
        var candidates = new List<string>();

        AddCandidate(candidates, WintunDllPath);
        AddCandidate(candidates, FlatWintunDllPath);

        var repoRoot = FindRepositoryRoot(RuntimeRoot);
        if (repoRoot is not null)
        {
            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "third_party",
                "wintun",
                "bin",
                "amd64",
                WintunDllName));

            AddCandidate(candidates, Path.Combine(
                repoRoot,
                "third_party",
                "wintun",
                WintunDllName));
        }

        return candidates;
    }

    private void AddRepoBuildCandidates(List<string> candidates, string projectName, string executableName)
    {
        var repoRoot = FindRepositoryRoot(RuntimeRoot);
        if (repoRoot is null)
        {
            return;
        }

        AddCandidate(candidates, Path.Combine(
            repoRoot,
            "src",
            projectName,
            "bin",
            "Debug",
            "net8.0-windows",
            executableName));

        AddCandidate(candidates, Path.Combine(
            repoRoot,
            "src",
            projectName,
            "bin",
            "Release",
            "net8.0-windows",
            executableName));
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

    private static string ResolveRuntimeRoot(string baseDirectory)
    {
        var current = new DirectoryInfo(baseDirectory);
        if (current.Name.Equals("system", StringComparison.OrdinalIgnoreCase) &&
            current.Parent is not null)
        {
            return current.Parent.FullName;
        }

        var repoRoot = FindRepositoryRoot(baseDirectory);
        if (repoRoot is not null &&
            TryGetBuildOutputInfo(repoRoot, baseDirectory, out var configuration, out var targetFramework))
        {
            var uiOutputRoot = Path.Combine(
                repoRoot,
                "src",
                "TunnelFlow.UI",
                "bin",
                configuration,
                targetFramework);

            if (Directory.Exists(uiOutputRoot))
            {
                return uiOutputRoot;
            }
        }

        return baseDirectory;
    }

    private static bool TryGetBuildOutputInfo(
        string repositoryRoot,
        string baseDirectory,
        out string configuration,
        out string targetFramework)
    {
        configuration = string.Empty;
        targetFramework = string.Empty;

        var relativePath = Path.GetRelativePath(repositoryRoot, baseDirectory);
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 5 ||
            !segments[0].Equals("src", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        configuration = segments[3];
        targetFramework = segments[4];
        return !string.IsNullOrWhiteSpace(configuration) && !string.IsNullOrWhiteSpace(targetFramework);
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
