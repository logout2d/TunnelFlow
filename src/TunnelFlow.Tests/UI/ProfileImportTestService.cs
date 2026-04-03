using TunnelFlow.Core.Models;
using TunnelFlow.UI.Services;

namespace TunnelFlow.Tests.UI;

internal sealed class ProfileImportTestService(Func<string, CancellationToken, Task<VlessProfile>> importAsync)
    : IProfileImportService
{
    public Task<VlessProfile> ImportFromUrlAsync(string url, CancellationToken cancellationToken) =>
        importAsync(url, cancellationToken);
}
