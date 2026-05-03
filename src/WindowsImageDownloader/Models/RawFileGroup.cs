namespace WindowsImageDownloader.Models;

/// <summary>
/// Represents a downloadable file with all Windows editions it contains.
/// </summary>
public sealed record RawFileGroup(
    RawFile File,
    IReadOnlyList<string> Editions);
