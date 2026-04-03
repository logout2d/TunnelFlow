using TunnelFlow.Core.Models;

namespace TunnelFlow.UI.Services;

public interface IProfileImportService
{
    Task<ProfileImportResult> ImportProfilesAsync(string url, CancellationToken cancellationToken);

    Task<VlessProfile> ImportFromUrlAsync(string url, CancellationToken cancellationToken);
}

public sealed record ProfileImportResult(IReadOnlyList<VlessProfile> Profiles, int SkippedProfileCount)
{
    public int ImportedProfileCount => Profiles.Count;
}
