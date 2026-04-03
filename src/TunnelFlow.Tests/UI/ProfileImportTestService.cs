using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.UI;

internal sealed class ProfileImportTestService(Func<string, CancellationToken, Task<ProfileImportResult>> importProfilesAsync)
    : IProfileImportService
{
    public Task<ProfileImportResult> ImportProfilesAsync(string url, CancellationToken cancellationToken) =>
        importProfilesAsync(url, cancellationToken);

    public async Task<VlessProfile> ImportFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        var result = await importProfilesAsync(url, cancellationToken);
        return result.Profiles.Single();
    }
}
