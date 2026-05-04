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
    // ── Identity / catalog fields (mirrors RawFile) ─────────────────────────

    /// <summary>SHA-256 hash of the ESD file. Primary key — matches RawFile.Sha256.</summary>
    public required string Sha256 { get; init; }

    /// <summary>BCP-47 language code, e.g. "zh-cn".</summary>
    public required string LanguageCode { get; init; }

    /// <summary>Human-readable language name, e.g. "Chinese (Simplified)".</summary>
    public required string Language { get; init; }

    /// <summary>Architecture string, e.g. "x64", "arm64".</summary>
    public required string Architecture { get; init; }

    /// <summary>Localised edition group label, e.g. "消费者零售版".</summary>
    public required string EditionLoc { get; init; }

    /// <summary>Edition name bundled inside the ESD representative entry.</summary>
    public required string Edition { get; init; }

    /// <summary>All edition names bundled inside the ESD.</summary>
    public required IReadOnlyList<string> Editions { get; init; }

    /// <summary>Original ESD filename from products.xml.</summary>
    public required string FileName { get; init; }

    /// <summary>Direct ESD download URL.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>Total file size in bytes as advertised in the catalog.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>Whether the ESD is retail-only.</summary>
    public required bool IsRetailOnly { get; init; }

    // ── Display helpers ──────────────────────────────────────────────────────

    /// <summary>Language label shown as a tag, e.g. "zh-cn".</summary>
    public string LanguageText => string.IsNullOrWhiteSpace(Language) ? LanguageCode : LanguageCode;

    /// <summary>Localised edition group label, mapping raw tokens to human-readable strings.</summary>
    public string EditionGroupText => EditionLoc switch
    {
        "%CLIENT%"       => "消费者零售版",
        "%ENTERPRISE%"   => "企业批量许可版",
        "%ENTERPRISE_N%" => "企业批量许可版 N",
        "%BASE_CHINA%"   => "中国特供版",
        ""               => "未分组",
        _                => EditionLoc,
    };

    /// <summary>Tag colour for the edition group.</summary>
    public TagType EditionGroupTagType => EditionLoc switch
    {
        "%CLIENT%"       => TagType.Success,
        "%ENTERPRISE%"   => TagType.Primary,
        "%ENTERPRISE_N%" => TagType.Primary,
        "%BASE_CHINA%"   => TagType.Warning,
        ""               => TagType.Info,
        _                => TagType.Danger,
    };

    /// <summary>Retail status label.</summary>
    public string RetailText => IsRetailOnly ? "零售版" : "通用";

    /// <summary>Tag colour for retail status.</summary>
    public TagType RetailTagType => IsRetailOnly ? TagType.Warning : TagType.Success;

    /// <summary>Tag colour for architecture.</summary>
    public TagType ArchTagType => Architecture.ToLowerInvariant() switch
    {
        "x64"   => TagType.Success,
        "arm64" => TagType.Danger,
        _       => TagType.Default,
    };

    /// <summary>Human-readable file size.</summary>
    public string SizeText => TotalBytes switch
    {
        < 1024                => $"{TotalBytes} B",
        < 1024 * 1024         => $"{TotalBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{TotalBytes / (1024.0 * 1024):F1} MB",
        _                     => $"{TotalBytes / (1024.0 * 1024 * 1024):F2} GB",
    };

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

    /// <summary>Creates a <see cref="DownloadTask"/> from a catalog entry and its full editions list.</summary>
    public static DownloadTask FromRawFile(RawFile file, IReadOnlyList<string> editions) => new()
    {
        Sha256       = file.Sha256,
        LanguageCode = file.LanguageCode,
        Language     = file.Language,
        Architecture = file.Architecture,
        EditionLoc   = file.EditionLoc,
        Edition      = file.Edition,
        Editions     = editions,
        FileName     = file.FileName,
        DownloadUrl  = file.FilePath,
        TotalBytes   = file.Size,
        IsRetailOnly = file.IsRetailOnly,
        State        = TaskState.Queued,
    };
}
