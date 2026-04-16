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

    private RuntimePaths(string baseDirectory, string dataRoot)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string BaseDirectory { get; }

    public string DataRoot { get; }

    public string CurrentConfigPath => Path.Combine(DataRoot, ConfigFileName);

    public string CurrentLogsRoot => Path.Combine(DataRoot, "logs");

    public string ServiceLogPath => Path.Combine(CurrentLogsRoot, ServiceLogFileName);

    public string SingBoxLogPath => Path.Combine(CurrentLogsRoot, SingBoxLogFileName);

    public string SingBoxConfigPath => Path.Combine(DataRoot, SingBoxConfigFileName);

    public string UiLogsRoot => Path.Combine(BaseDirectory, "logs");

    public string UiLogPath => Path.Combine(UiLogsRoot, UiLogFileName);

    public string ConfigRoot => Path.Combine(BaseDirectory, "config");

    public string AppSettingsPath => Path.Combine(ConfigRoot, "appsettings.json");

    public string SystemRoot => Path.Combine(BaseDirectory, "system");

    public string ServiceExecutablePath => Path.Combine(SystemRoot, ServiceExecutableName);

    public string BootstrapperExecutablePath => Path.Combine(SystemRoot, BootstrapperExecutableName);

    public string CoreRoot => Path.Combine(BaseDirectory, "core");

    public string SingBoxExecutablePath => Path.Combine(CoreRoot, SingBoxExecutableName);

    public string WintunDllPath => Path.Combine(CoreRoot, WintunDllName);

    public string FlatServiceExecutablePath => Path.Combine(BaseDirectory, ServiceExecutableName);

    public string FlatBootstrapperExecutablePath => Path.Combine(BaseDirectory, BootstrapperExecutableName);

    public string FlatSingBoxExecutablePath => Path.Combine(BaseDirectory, SingBoxExecutableName);

    public string FlatWintunDllPath => Path.Combine(BaseDirectory, WintunDllName);

    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductName);

    public static RuntimePaths Current => Create();

    public static RuntimePaths Create(string? baseDirectory = null, string? dataRoot = null) =>
        new(baseDirectory ?? AppContext.BaseDirectory, dataRoot ?? DefaultDataRoot);

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

        var repoRoot = FindRepositoryRoot(BaseDirectory);
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

        var repoRoot = FindRepositoryRoot(BaseDirectory);
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
        var repoRoot = FindRepositoryRoot(BaseDirectory);
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

    private static void AddCandidate(List<string> candidates, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
        {
            candidates.Add(fullPath);
        }
    }
}
