using Microsoft.Extensions.Hosting;
using WinDownloader.Models;
using WinDownloader.Services;

namespace WinDownloader.Interfaces;

/// <summary>
/// Orchestrates user-triggered ESD to ISO conversion workers.
/// </summary>
public interface IEsdToIsoOrchestratorService : IHostedService
{
    /// <summary>
    /// Queues ISO conversion for a completed ESD download task.
    /// </summary>
    Task<TaskOperationResult> ConvertToIsoAsync(DownloadTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the task currently has a queued or running ISO conversion worker.
    /// </summary>
    bool IsConversionQueuedOrRunning(string sha256);

    /// <summary>
    /// Gets the latest in-memory ISO conversion snapshot for a task.
    /// </summary>
    EsdToIsoTaskSnapshot? GetSnapshot(string sha256);

    /// <summary>
    /// Clears the latest in-memory ISO conversion snapshot for a task.
    /// </summary>
    void ClearSnapshot(string sha256);

    /// <summary>
    /// Number of currently active ISO conversion workers.
    /// </summary>
    int ActiveTaskCount { get; }

    /// <summary>Raised when an ISO conversion snapshot changes. Handlers may be called from background threads.</summary>
    event EventHandler<IsoConversionTaskSnapshot> ConversionChanged;

    /// <summary>Raised when <see cref="ActiveTaskCount"/> changes. Handlers may be called from background threads.</summary>
    event EventHandler? ActiveTaskCountChanged;
}