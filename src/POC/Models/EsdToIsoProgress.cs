namespace POC.Models;

public sealed record EsdToIsoProgress(
    EsdToIsoStage Stage,
    string Message,
    int? CurrentImageIndex,
    int? TotalImageCount,
    IsoCreationBackend? Backend,
    string? CurrentFile,
    double? Percent,
    TimeSpan Elapsed,
    WimOperationProgress? WimProgress = null);
