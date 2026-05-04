namespace POC.Wim.Models;

public sealed record IsoCreationResult(
    IsoCreationBackend Backend,
    string OutputIsoPath,
    bool Succeeded,
    bool Skipped,
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
    public static IsoCreationResult Skip(
        IsoCreationBackend backend,
        string outputIsoPath,
        string message,
        IReadOnlyList<string>? warnings = null)
    {
        return new IsoCreationResult(
            backend,
            outputIsoPath,
            Succeeded: false,
            Skipped: true,
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
