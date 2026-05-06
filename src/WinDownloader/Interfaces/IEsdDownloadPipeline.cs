using WinDownloader.Models;

namespace WinDownloader.Interfaces;

/// <summary>
/// Executes the file-level ESD download and SHA-256 verification steps for a task.
/// </summary>
public interface IEsdDownloadPipeline
{
    /// <summary>
    /// Downloads the task's ESD file to its resolved destination path.
    /// </summary>
    Task DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the downloaded ESD file against <see cref="DownloadTask.Sha256"/>.
    /// Throws when verification fails.
    /// </summary>
    Task VerifyAsync(DownloadTask task, CancellationToken cancellationToken = default);
}
