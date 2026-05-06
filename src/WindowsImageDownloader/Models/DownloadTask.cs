namespace WindowsImageDownloader.Models;

/// <summary>
/// Represents a single ESD download task.
/// <para>
/// <see cref="Sha256"/> is the natural primary key. It matches <see cref="RawFile.Sha256"/>
/// from the product catalog and is globally unique per ESD file.
/// </para>
/// </summary>
public sealed class DownloadTask
{
    // ── Identity / catalog payload ──────────────────────────────────────────

    /// <summary>Original catalog file group this task downloads.</summary>
    public required RawFileGroup FileGroup { get; init; }

    /// <summary>SHA-256 hash of the ESD file. Primary key — matches RawFile.Sha256.</summary>
    public string Sha256 => FileGroup.File.Sha256;

    /// <summary>BCP-47 language code, e.g. "zh-cn".</summary>
    public string LanguageCode => FileGroup.File.LanguageCode;

    /// <summary>Human-readable language name, e.g. "Chinese (Simplified)".</summary>
    public string Language => FileGroup.File.Language;

    /// <summary>Architecture string, e.g. "x64", "arm64".</summary>
    public string Architecture => FileGroup.File.Architecture;

    /// <summary>Localised edition group label, e.g. "消费者零售版".</summary>
    public string EditionLoc => FileGroup.File.EditionLoc;

    /// <summary>Edition name bundled inside the ESD representative entry.</summary>
    public string Edition => FileGroup.File.Edition;

    /// <summary>All edition names bundled inside the ESD.</summary>
    public IReadOnlyList<string> Editions => FileGroup.Editions;

    /// <summary>Original ESD filename from products.xml.</summary>
    public string FileName => FileGroup.File.FileName;

    /// <summary>Direct ESD download URL.</summary>
    public string DownloadUrl => FileGroup.File.FilePath;

    /// <summary>Total file size in bytes as advertised in the catalog.</summary>
    public long TotalBytes => FileGroup.File.Size;

    /// <summary>Whether the ESD is retail-only.</summary>
    public bool IsRetailOnly => FileGroup.File.IsRetailOnly;

    // ── Runtime state ────────────────────────────────────────────────────────

    /// <summary>Current lifecycle state of this task.</summary>
    public TaskState State { get; set; }

    /// <summary>Number of bytes already written to disk.</summary>
    public long DownloadedBytes { get; set; }

    /// <summary>Current task progress in the range [0, 1].</summary>
    public double Progress { get; set; }

    /// <summary>Current download speed in bytes per second. 0 when not downloading.</summary>
    public long SpeedBytesPerSecond { get; set; }

    /// <summary>Human-readable status line shown beneath the progress bar.</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>Error message when <see cref="State"/> is <see cref="TaskState.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>UTC time when this task was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC time of the last state or progress change.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Creates a <see cref="DownloadTask"/> from a catalog file group.</summary>
    public static DownloadTask FromRawFileGroup(RawFileGroup fileGroup) => new()
    {
        FileGroup = fileGroup,
        State     = TaskState.Queued,
    };
}
