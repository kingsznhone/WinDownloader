namespace POC.Wim.Models;

public sealed record EsdToIsoResult(
    string SourceEsdPath,
    string RunDirectory,
    string StagingDirectory,
    string BootWimPath,
    IReadOnlyList<string> InstallImagePaths,
    IReadOnlyList<IsoCreationResult> IsoResults,
    IReadOnlyList<WimImageInfo> SourceImages,
    IReadOnlyList<string> Warnings,
    string EventsPath,
    string ManifestPath,
    string SummaryPath,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
}
