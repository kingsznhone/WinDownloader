using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Interfaces;

public interface IWimProcessingService
{
    Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken cancellationToken = default);

    Task ExtractImageAsync(
        WimExtractRequest request,
        Action<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task ExportImagesAsync(
        WimExportRequest request,
        Action<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
