namespace TunnelFlow.Service.Tun;

public static class WintunPathResolver
{
    public static string Resolve(string? baseDirectory = null)
    {
        var root = baseDirectory ?? AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..",
                "third_party", "wintun", "bin", "amd64", "wintun.dll")),
            Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "..",
                "third_party", "wintun", "wintun.dll")),
            Path.Combine(root, "wintun.dll")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
