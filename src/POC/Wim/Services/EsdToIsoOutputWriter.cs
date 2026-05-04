using System.Text.Json;
using POC.Wim.Models;

namespace POC.Wim.Services;

internal static class EsdToIsoOutputWriter
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static async Task WriteManifestAsync(EsdToIsoResult result, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(result.ManifestPath);
        await JsonSerializer.SerializeAsync(stream, result, ManifestJsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteSummaryAsync(EsdToIsoResult result, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(result.SummaryPath);
        await using var writer = new StreamWriter(stream);

        await writer.WriteLineAsync($"Source: {result.SourceEsdPath}").ConfigureAwait(false);
        await writer.WriteLineAsync($"Run: {result.RunDirectory}").ConfigureAwait(false);
        await writer.WriteLineAsync($"Staging: {result.StagingDirectory}").ConfigureAwait(false);
        await writer.WriteLineAsync($"boot.wim: {result.BootWimPath}").ConfigureAwait(false);

        foreach (var installImagePath in result.InstallImagePaths)
        {
            await writer.WriteLineAsync($"Install image: {installImagePath}").ConfigureAwait(false);
        }

        foreach (var isoResult in result.IsoResults)
        {
            var state = isoResult.Succeeded ? "succeeded" : isoResult.Skipped ? "skipped" : "failed";
            await writer.WriteLineAsync($"ISO {isoResult.Backend}: {state} -> {isoResult.OutputIsoPath}").ConfigureAwait(false);
        }

        foreach (var warning in result.Warnings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync($"Warning: {warning}").ConfigureAwait(false);
        }
    }
}
