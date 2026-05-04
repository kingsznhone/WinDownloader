using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Interfaces;

public interface IUpdateCatalogService
{
    Task<IReadOnlyList<RawFile>> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
