using Microsoft.Extensions.Hosting;
using WinDownloader.Models;
using WinDownloader.Services;

namespace WinDownloader.Interfaces;

/// <summary>
/// Orchestrates ESD download tasks: queue → download → SHA-256 verify → complete.
/// <para>
/// Implements <see cref="IHostedService"/>: <c>StartAsync</c> loads persisted tasks;
/// <c>StopAsync</c> signals active downloads to shut down.
/// </para>
/// </summary>
public interface ITaskOrchestratorService : IHostedService
{
    // ── Queue management ──────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a new ESD download task. The task is persisted to the cache and
    /// immediately scheduled for downloading.
    /// </summary>
    Task<TaskOperationResult> EnqueueAsync(DownloadTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enqueues a failed or completed task from scratch.
    /// Resets <see cref="DownloadTask.DownloadedBytes"/> to 0.
    /// </summary>
    Task RequeueAsync(string sha256, CancellationToken cancellationToken = default);

    // ── Task control ──────────────────────────────────────────────────────────

    /// <summary>
    /// Pauses an active download. The task transitions to <see cref="TaskState.Queued"/>
    /// and can be resumed by calling <see cref="ResumeAsync"/>.
    /// </summary>
    Task<TaskOperationResult> PauseAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a queued task, continuing from the last downloaded byte when possible.
    /// </summary>
    Task<TaskOperationResult> ResumeAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels and removes a task. Partial ESD download files are deleted.
    /// </summary>
    Task<TaskOperationResult> CancelAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues ISO conversion for a completed ESD download task.
    /// </summary>
    Task<TaskOperationResult> ConvertToIsoAsync(string sha256, CancellationToken cancellationToken = default);

    // ── Removal ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fully removes a completed task and deletes the verified ESD file from disk.
    /// The database record is also deleted and <see cref="TaskRemoved"/> is raised.
    /// </summary>
    Task<TaskOperationResult> DeleteAsync(string sha256, CancellationToken cancellationToken = default);

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>
    /// All tasks currently tracked by the orchestrator, ordered by
    /// <see cref="DownloadTask.CreatedAt"/> descending.
    /// </summary>
    IReadOnlyList<DownloadTask> Tasks { get; }

    /// <summary>
    /// Number of currently active download or ISO conversion workers.
    /// </summary>
    int ActiveTaskCount { get; }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Raised on the calling thread when a new task is added to <see cref="Tasks"/>.</summary>
    event EventHandler<DownloadTask> TaskAdded;

    /// <summary>Raised on the calling thread when a task is removed from <see cref="Tasks"/>.</summary>
    event EventHandler<DownloadTask> TaskRemoved;

    /// <summary>Raised when task state or progress changes. Handlers may be called from background threads.</summary>
    event EventHandler<DownloadTaskSnapshot> TaskChanged;

    /// <summary>Raised when <see cref="ActiveTaskCount"/> changes. Handlers may be called from background threads.</summary>
    event EventHandler? ActiveTaskCountChanged;
}
