using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Interfaces;

public interface IIsoCreationService
{
    Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default);
}
