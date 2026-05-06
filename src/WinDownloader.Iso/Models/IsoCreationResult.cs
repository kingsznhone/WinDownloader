namespace WinDownloader.Iso;

public sealed record IsoCreationResult(
    string OutputIsoPath,
    bool Succeeded,
    TimeSpan Duration,
    long OutputSize,
    string? ToolPath,
    string? CommandLine,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    IReadOnlyList<string> Warnings,
    string? ErrorMessage)
{
    public static IsoCreationResult Failure(
        string outputIsoPath,
        string message,
        IReadOnlyList<string>? warnings = null)
    {
        return new IsoCreationResult(
            outputIsoPath,
            Succeeded: false,
            Duration: TimeSpan.Zero,
            OutputSize: 0,
            ToolPath: null,
            CommandLine: null,
            ExitCode: null,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            warnings ?? Array.Empty<string>(),
            message);
    }
}
