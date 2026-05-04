using POC.Wim.Models;

namespace POC.Wim.Interfaces;

public interface IWimProcessingService
{
    Task<WimLibraryInfo> GetLibraryInfoAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken cancellationToken = default);

    Task ExtractImageAsync(
        string imagePath,
        int imageIndex,
        string destinationDirectory,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task ExportImageAsync(
        WimExportRequest request,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task ExportImagesAsync(
        WimMultiImageExportRequest request,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
