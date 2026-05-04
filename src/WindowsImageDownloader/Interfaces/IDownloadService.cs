using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Interfaces;

/// <summary>
/// Provides resumable HTTP download capability with progress reporting.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Downloads a file to <paramref name="destinationPath"/> with resume support.
    /// <para>
    /// If the destination file already exists, a <c>Range</c> request is issued
    /// to continue from the last written byte. The caller is responsible for
    /// SHA-256 verification after completion.
    /// </para>
    /// </summary>
    /// <param name="url">Direct download URL.</param>
    /// <param name="destinationPath">Absolute path to write the file.</param>
    /// <param name="progress">
    /// Receives periodic <see cref="DownloadProgress"/> snapshots.
    /// </param>
    /// <param name="cancellationToken">Token to cancel or pause the download.</param>
    Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A point-in-time snapshot of download progress.
/// </summary>
/// <param name="DownloadedBytes">Bytes written to disk so far (including any pre-existing resume data).</param>
/// <param name="TotalBytes">Total expected file size, or -1 if unknown.</param>
/// <param name="SpeedBytesPerSecond">Instantaneous transfer rate.</param>
/// <param name="Elapsed">Wall-clock time since the current download session started.</param>
public sealed record DownloadProgress(
    long DownloadedBytes,
    long TotalBytes,
    long SpeedBytesPerSecond,
    TimeSpan Elapsed)
{
    /// <summary>Progress ratio in [0, 1], or NaN when total size is unknown.</summary>
    public double Ratio => TotalBytes > 0 ? (double)DownloadedBytes / TotalBytes : double.NaN;

    /// <summary>Estimated time remaining, or null when speed or total size is unknown.</summary>
    public TimeSpan? Eta => TotalBytes > 0 && SpeedBytesPerSecond > 0
        ? TimeSpan.FromSeconds((double)(TotalBytes - DownloadedBytes) / SpeedBytesPerSecond)
        : null;
}
