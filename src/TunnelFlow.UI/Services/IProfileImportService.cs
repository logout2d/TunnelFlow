using TunnelFlow.Core.Models;

namespace TunnelFlow.UI.Services;

public interface IProfileImportService
{
    Task<VlessProfile> ImportFromUrlAsync(string url, CancellationToken cancellationToken);
}
