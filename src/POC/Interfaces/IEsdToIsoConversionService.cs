using POC.Models;

namespace POC.Interfaces;

public interface IEsdToIsoConversionService
{
    event EventHandler<EsdToIsoTaskSnapshot>? ProgressChanged;

    Task<EsdToIsoResult> ConvertAsync(
        EsdToIsoRequest request,
        CancellationToken cancellationToken = default);
}
