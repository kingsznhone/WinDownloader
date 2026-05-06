namespace WindowsImageDownloader.Iso;

public interface IIsoCreationService
{
    Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default);
}
