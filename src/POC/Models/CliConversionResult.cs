namespace POC.Models;

public sealed record CliConversionResult(
    bool Succeeded,
    string StagingDirectory,
    string BootWimPath,
    string InstallWimPath,
    string IsoPath,
    TimeSpan Duration,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings);
