using POC.Wim.Models;

namespace POC.Wim.Services;

internal static class EsdToIsoProgressFactory
{
    public static EsdToIsoProgress CreateProgress(
        EsdToIsoStage stage,
        string message,
        int? currentImageIndex = null,
        int? totalImageCount = null,
        string? currentFile = null,
        double? percent = null,
        WimOperationProgress? wimProgress = null,
        IsoCreationBackend? backend = null)
    {
        return new EsdToIsoProgress(stage, message, currentImageIndex, totalImageCount, backend, currentFile, percent, TimeSpan.Zero, wimProgress);
    }

    public static IProgress<WimOperationProgress> CreateWimProgress(Action<WimOperationProgress> report)
    {
        return new ActionProgress<WimOperationProgress>(report);
    }

    public static double? StagePercent(EsdToIsoStage stage, double? nestedPercent)
    {
        var (start, end) = stage switch
        {
            EsdToIsoStage.ApplyingSetupMedia => (8d, 30d),
            EsdToIsoStage.BuildingBootWim => (30d, 50d),
            EsdToIsoStage.BuildingInstallImage => (50d, 85d),
            EsdToIsoStage.CreatingIso => (85d, 96d),
            _ => (0d, 100d)
        };

        return nestedPercent is null
            ? start
            : Math.Clamp(start + (end - start) * nestedPercent.Value / 100d, start, end);
    }
}
