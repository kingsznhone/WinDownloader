using POC.Wim.Models;

namespace POC.Wim.Interfaces;

public interface IEsdToIsoPipelineService
{
    Task<EsdToIsoResult> BuildAsync(
        EsdToIsoRequest request,
        IProgress<EsdToIsoProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
