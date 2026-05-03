using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

public interface IUpdateCatalogService
{
    Task<IReadOnlyList<RawFile>> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
