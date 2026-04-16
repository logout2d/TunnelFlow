using TunnelFlow.Core;

namespace TunnelFlow.Service.Tun;

public static class WintunPathResolver
{
    public static string Resolve(string? baseDirectory = null)
    {
        var candidates = RuntimePaths.Create(baseDirectory).GetWintunDllCandidates();

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
