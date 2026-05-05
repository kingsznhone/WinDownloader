using POC.Models;

namespace POC.Interfaces;

public interface IIsoCreationService
{
    Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default);
}
