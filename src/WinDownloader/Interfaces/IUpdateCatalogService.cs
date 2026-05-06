using WinDownloader.Models;

namespace WinDownloader.Interfaces;

public interface IUpdateCatalogService
{
    Task<IReadOnlyList<RawFile>> GetCatalogAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}
