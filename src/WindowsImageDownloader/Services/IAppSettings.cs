using System.ComponentModel;

namespace WindowsImageDownloader.Services;

/// <summary>
/// Application settings interface for WindowsImageDownloader.
/// </summary>
public interface IAppSettings : INotifyPropertyChanged
{
    /// <summary>
    /// Number of chunks to split a download into for multi-threaded downloading.
    /// Default: 4. Clamped to 1–32.
    /// </summary>
    int DownloadChunkCount { get; set; }

    /// <summary>
    /// Maximum number of download tasks running concurrently.
    /// Default: 2. Clamped to 1–16.
    /// </summary>
    int MaxConcurrentDownloads { get; set; }

    /// <summary>
    /// Directory where downloaded ESD files and converted output are saved.
    /// Falls back to default download folder when null or empty.
    /// </summary>
    string? DownloadDirectory { get; set; }

    /// <summary>
    /// UI culture override (e.g. "zh-CN", "en-US"). Null means follow system.
    /// </summary>
    string? AppLanguage { get; set; }

    /// <summary>
    /// Resolves the effective UI language from saved setting, system locale, or fallback.
    /// </summary>
    string ResolveEffectiveLanguage();

    /// <summary>
    /// Writes default values for every key that is not yet present in the store.
    /// Call once at application start-up.
    /// </summary>
    void EnsureDefaults();

    /// <summary>
    /// Resets all settings back to their defaults.
    /// </summary>
    void Reset();
}
