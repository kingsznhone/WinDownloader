using POC.Models;

namespace POC.Interfaces;

public interface IEsdToIsoPipelineService
{
    Task<EsdToIsoResult> BuildAsync(
        EsdToIsoRequest request,
        IProgress<EsdToIsoProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
