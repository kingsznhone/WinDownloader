namespace WinDownloader.Models;

/// <summary>
/// Lifecycle state of an ESD download task.
/// </summary>
public enum TaskState
{
    /// <summary>Waiting in the download queue.</summary>
    Queued,

    /// <summary>Actively downloading the ESD file.</summary>
    Downloading,

    /// <summary>Download finished; verifying SHA-256 checksum.</summary>
    Verifying,

    /// <summary>ESD file downloaded and verified successfully.</summary>
    Completed,

    /// <summary>A non-recoverable error occurred. See <see cref="DownloadTask.ErrorMessage"/>.</summary>
    Failed,
}
