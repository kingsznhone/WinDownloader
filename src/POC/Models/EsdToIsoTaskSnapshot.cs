namespace POC.Models;

public sealed record EsdToIsoTaskSnapshot(
    string TaskId,
    string SourceEsdPath,
    EsdToIsoTaskState State,
    EsdToIsoStage Stage,
    double Progress,
    string StatusText,
    string? CurrentFile,
    string? ErrorMessage,
    string IsoPath,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    TimeSpan Elapsed,
    WimOperationProgress? WimProgress = null);
