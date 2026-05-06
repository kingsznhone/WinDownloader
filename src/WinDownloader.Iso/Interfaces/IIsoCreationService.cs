namespace WinDownloader.Iso.Interfaces;

public interface IIsoCreationService
{
    Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default);
}
