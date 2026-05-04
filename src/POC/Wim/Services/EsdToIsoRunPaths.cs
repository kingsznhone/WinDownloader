namespace POC.Wim.Services;

internal sealed record EsdToIsoRunPaths(
    string RunDirectory,
    string StagingDirectory,
    string EventsPath,
    string ManifestPath,
    string SummaryPath);
