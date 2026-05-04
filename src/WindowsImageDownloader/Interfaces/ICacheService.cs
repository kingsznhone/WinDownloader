using Microsoft.Extensions.Hosting;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Interfaces;

/// <summary>
/// Persists and retrieves <see cref="DownloadTask"/> records from local storage (SQLite).
/// <para>
/// The cache is the single source of truth for task history and download resumption state.
/// File existence is always verified at runtime against the file system — never cached here.
/// </para>
/// <para>
/// Implements <see cref="IHostedService"/>: <c>StartAsync</c> creates the database schema
/// on application start-up so callers never need to invoke <see cref="EnsureSchemaAsync"/>
/// directly.
/// </para>
/// </summary>
public interface ICacheService : IHostedService
{
    // ── Task CRUD ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a new task. Throws <see cref="InvalidOperationException"/> if a task
    /// with the same <see cref="DownloadTask.Sha256"/> already exists.
    /// </summary>
    Task AddTaskAsync(DownloadTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the persisted state of an existing task (state, downloaded bytes,
    /// timestamps, error message). Identity fields are never updated.
    /// </summary>
    Task UpdateTaskAsync(DownloadTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all persisted tasks ordered by <see cref="DownloadTask.CreatedAt"/> descending.
    /// </summary>
    Task<IReadOnlyList<DownloadTask>> GetAllTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single task by its SHA-256 primary key, or <see langword="null"/> if not found.
    /// </summary>
    Task<DownloadTask?> GetTaskAsync(string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes the task record. Does not touch any files on disk.
    /// </summary>
    Task DeleteTaskAsync(string sha256, CancellationToken cancellationToken = default);

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the database schema if it does not yet exist.
    /// Safe to call on every application start.
    /// </summary>
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
}
