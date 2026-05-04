using POC.Models;

namespace POC.Interfaces;

public interface IIsoCreationService
{
    IsoCreationBackend Backend { get; }

    Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        IProgress<EsdToIsoProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
