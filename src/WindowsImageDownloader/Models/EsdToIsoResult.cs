namespace WindowsImageDownloader.Models;

public sealed record EsdToIsoResult(
    string SourceEsdPath,
    string StagingDirectory,
    string BootWimPath,
    string InstallEsdPath,
    string IsoPath,
    IsoCreationResult? IsoResult,
    IReadOnlyList<WimImageInfo> SourceImages,
    IReadOnlyList<string> Warnings,
    bool Succeeded,
    string? ErrorMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
}
