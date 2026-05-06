using WinDownloader.Wim;

namespace WinDownloader.Iso;

public enum EsdToIsoTaskState
{
    NotStarted,
    Running,
    Completed,
    Failed,
    Canceled
}

public enum EsdToIsoStage
{
    Preparing,
    InspectingSource,
    ApplyingSetupMedia,
    BuildingBootWim,
    BuildingInstallImage,
    CreatingIso,
    Completed,
    Failed
}

public sealed record EsdToIsoTaskSnapshot(
    string TaskId,
    string SourceEsdPath,
    EsdToIsoTaskState State,
    EsdToIsoStage Stage,
    double Progress,
    string? CurrentFile,
    string? ErrorMessage,
    string IsoPath,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Elapsed,
    WimOperationProgress? WimProgress = null,
    IsoOperationProgress? IsoProgress = null);
