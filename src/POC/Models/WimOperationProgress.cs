namespace POC.Models;

public sealed record WimOperationProgress(
    WimOperationStage Stage,
    double? Percent,
    ulong? CompletedBytes,
    ulong? TotalBytes,
    string? CurrentItem);

public enum WimOperationStage
{
    Opening,
    Extracting,
    Writing,
    Verifying,
    Metadata,
    Completed,
    Other
}
