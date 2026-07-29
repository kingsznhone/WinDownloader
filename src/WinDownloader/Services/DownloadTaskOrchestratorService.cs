using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using WinDownloader.Interfaces;
using WinDownloader.Models;

namespace WinDownloader.Services;

/// <summary>
/// Implements <see cref="IDownloadTaskOrchestratorService"/> for ESD download tasks.
/// </summary>
public sealed class DownloadTaskOrchestratorService : IDownloadTaskOrchestratorService, IAsyncDisposable
{
    private readonly ICacheService _cache;
    private readonly IEsdDownloadPipeline _downloadPipeline;
    private readonly IDownloadTaskPathService _pathService;
    private readonly IAppSettings _settings;
    private readonly IEsdToIsoOrchestratorService _isoOrchestrator;

    // ── In-memory task registry ───────────────────────────────────────────────
    private readonly ObservableCollection<DownloadTask> _tasks = [];
    private readonly ConcurrentDictionary<string, DownloadTask> _taskMap = new(StringComparer.OrdinalIgnoreCase);

    // ── Download workers ──────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _cancelledTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _downloadWorkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdownCts = new();
    private static readonly TimeSpan _downloadSlotPollInterval = TimeSpan.FromMilliseconds(250);
    private int _activeDownloadSlotCount;
    private int _activeDownloadCount;
    private bool _disposed;

    public DownloadTaskOrchestratorService(
        ICacheService cache,
        IEsdDownloadPipeline downloadPipeline,
        IDownloadTaskPathService pathService,
        IAppSettings settings,
        IEsdToIsoOrchestratorService isoOrchestrator)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(downloadPipeline);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(isoOrchestrator);

        _cache = cache;
        _downloadPipeline = downloadPipeline;
        _pathService = pathService;
        _settings = settings;
        _isoOrchestrator = isoOrchestrator;
    }

    // ── IHostedService ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var persisted = await _cache.GetAllTasksAsync(cancellationToken).ConfigureAwait(false);

        foreach (var task in persisted)
        {
            if (task.State is TaskState.Downloading or TaskState.Verifying)
            {
                task.State = TaskState.Queued;
                task.StatusText = string.Empty;
                task.SpeedBytesPerSecond = 0;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await _cache.UpdateTaskAsync(task, cancellationToken).ConfigureAwait(false);
            }
            else if (task.State == TaskState.Completed && !File.Exists(_pathService.ResolveEsdPath(task)))
            {
                task.State = TaskState.Queued;
                task.DownloadedBytes = 0;
                task.Progress = 0;
                task.SpeedBytesPerSecond = 0;
                task.StatusText = "Local file missing, please re-download";
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await _cache.UpdateTaskAsync(task, cancellationToken).ConfigureAwait(false);
            }

            _taskMap[task.Sha256] = task;
            _tasks.Add(task);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        var workers = _downloadWorkers.Values.ToArray();
        if (workers.Length > 0)
            await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── IDownloadTaskOrchestratorService ─────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<DownloadTask> Tasks => _tasks;

    /// <inheritdoc/>
    public int ActiveTaskCount => Volatile.Read(ref _activeDownloadCount);

    /// <inheritdoc/>
    public event EventHandler<DownloadTask>? TaskAdded;

    /// <inheritdoc/>
    public event EventHandler<DownloadTask>? TaskRemoved;

    /// <inheritdoc/>
    public event EventHandler<DownloadTaskSnapshot>? TaskChanged;

    /// <inheritdoc/>
    public event EventHandler? ActiveTaskCountChanged;

    /// <inheritdoc/>
    public async Task<TaskOperationResult> EnqueueAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (_taskMap.TryGetValue(task.Sha256, out var existing))
        {
            return TaskOperationResult.Failure(BuildDuplicateTaskMessage(existing));
        }

        if (!_taskMap.TryAdd(task.Sha256, task))
        {
            return _taskMap.TryGetValue(task.Sha256, out existing)
                ? TaskOperationResult.Failure(BuildDuplicateTaskMessage(existing))
                : TaskOperationResult.Failure("Task already exists.");
        }

        try
        {
            await _cache.AddTaskAsync(task, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            _taskMap.TryRemove(task.Sha256, out _);
            return TaskOperationResult.Failure("Task already exists.");
        }
        catch
        {
            _taskMap.TryRemove(task.Sha256, out _);
            throw;
        }

        _tasks.Insert(0, task);
        TaskAdded?.Invoke(this, task);

        await ScheduleDownloadAsync(task).ConfigureAwait(false);
        return TaskOperationResult.Success("Task added.");
    }

    /// <inheritdoc/>
    public async Task RequeueAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            throw new InvalidOperationException($"Task '{sha256}' not found.");

        task.DownloadedBytes = 0;
        task.Progress = 0;
        task.SpeedBytesPerSecond = 0;
        task.ErrorMessage = null;
        task.State = TaskState.Queued;
        task.StatusText = string.Empty;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await _cache.UpdateTaskAsync(task, cancellationToken).ConfigureAwait(false);
        PublishTaskChanged(task);
        await ScheduleDownloadAsync(task).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<TaskOperationResult> PauseAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            return Task.FromResult(TaskOperationResult.Failure("Task not found, unable to pause."));

        if (task.State != TaskState.Downloading)
            return Task.FromResult(TaskOperationResult.Failure($"Can only pause tasks that are downloading, current state is {task.State}."));

        if (!_activeCts.TryGetValue(sha256, out var cts))
            return Task.FromResult(TaskOperationResult.Failure("No active download stream to pause."));

        task.State = TaskState.Queued;
        task.StatusText = "Pausing...";
        task.SpeedBytesPerSecond = 0;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        PublishTaskChanged(task);
        RequestCancellation(cts);
        return Task.FromResult(TaskOperationResult.Success());
    }

    /// <inheritdoc/>
    public async Task<TaskOperationResult> ResumeAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            return TaskOperationResult.Failure("Task not found, unable to resume.");

        if (task.State != TaskState.Queued)
            return TaskOperationResult.Failure($"Can only resume tasks that are paused or queued.");

        await ScheduleDownloadAsync(task).ConfigureAwait(false);
        return TaskOperationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<TaskOperationResult> CancelAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            return TaskOperationResult.Failure("Task not found, unable to cancel.");

        if (task.State is not (TaskState.Queued or TaskState.Downloading or TaskState.Verifying or TaskState.Failed))
            return TaskOperationResult.Failure($"Current state is {task.State}, unable to cancel the task.");

        if (_activeCts.TryRemove(sha256, out var cts))
        {
            _cancelledTasks[sha256] = 0;
            RequestCancellation(cts);
        }

        TryDeleteFile(_pathService.ResolveTemporaryDownloadPath(task));

        await _cache.DeleteTaskAsync(sha256, cancellationToken).ConfigureAwait(false);
        _taskMap.TryRemove(sha256, out _);

        var item = _tasks.FirstOrDefault(t => t.Sha256 == sha256);
        if (item is not null)
        {
            _tasks.Remove(item);
            TaskRemoved?.Invoke(this, item);
        }

        return TaskOperationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<TaskOperationResult> DeleteAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            return TaskOperationResult.Failure("Task not found, unable to delete.");

        if (task.State != TaskState.Completed)
            return TaskOperationResult.Failure($"Can only delete tasks that are completed.");

        if (_isoOrchestrator.IsConversionQueuedOrRunning(sha256))
            return TaskOperationResult.Failure("ISO conversion is not yet complete, please wait until it finishes before deleting the file.");

        TryDeleteFile(_pathService.ResolveEsdPath(task));
        _isoOrchestrator.ClearSnapshot(sha256);

        await _cache.DeleteTaskAsync(sha256, cancellationToken).ConfigureAwait(false);
        _taskMap.TryRemove(sha256, out _);

        var item = _tasks.FirstOrDefault(t => t.Sha256 == sha256);
        if (item is not null)
        {
            _tasks.Remove(item);
            TaskRemoved?.Invoke(this, item);
        }

        return TaskOperationResult.Success();
    }

    // ── Download worker ───────────────────────────────────────────────────────

    private Task ScheduleDownloadAsync(DownloadTask task)
    {
        if (_downloadWorkers.TryGetValue(task.Sha256, out var existingWorker))
        {
            _ = existingWorker.ContinueWith(async _ =>
            {
                TryRemoveDownloadWorker(task.Sha256, existingWorker);
                if (_taskMap.TryGetValue(task.Sha256, out var currentTask) && currentTask.State == TaskState.Queued)
                    await ScheduleDownloadAsync(currentTask).ConfigureAwait(false);
            }, TaskScheduler.Default).Unwrap();
            return Task.CompletedTask;
        }

        var worker = Task.Run(() => ProcessDownloadAsync(task, _shutdownCts.Token));
        if (!_downloadWorkers.TryAdd(task.Sha256, worker))
            return Task.CompletedTask;

        _ = worker.ContinueWith(_ => TryRemoveDownloadWorker(task.Sha256, worker), TaskScheduler.Default);
        return Task.CompletedTask;
    }

    private async Task ProcessDownloadAsync(DownloadTask task, CancellationToken shutdownToken)
    {
        var acquiredSlot = false;
        var countedActive = false;
        CancellationTokenSource? cts = null;

        try
        {
            acquiredSlot = await WaitForDownloadSlotAsync(task, shutdownToken).ConfigureAwait(false);
            if (!acquiredSlot || !_taskMap.ContainsKey(task.Sha256))
                return;

            countedActive = true;
            IncrementActiveTaskCount();

            cts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            _activeCts[task.Sha256] = cts;

            if (task.State != TaskState.Queued)
                return;

            task.State = TaskState.Downloading;
            task.StatusText = "Downloading...";
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);

            var progress = new Progress<DownloadProgress>(p =>
            {
                var now = DateTimeOffset.UtcNow;
                task.DownloadedBytes = p.DownloadedBytes;
                task.Progress = p.Ratio;
                task.SpeedBytesPerSecond = p.SpeedBytesPerSecond;
                task.UpdatedAt = now;

                var eta = p.Eta.HasValue
                    ? $"  Remaining {p.Eta.Value:mm\\:ss}"
                    : string.Empty;
                task.StatusText = $"{FormatBytes(p.SpeedBytesPerSecond)}/s{eta}";
                PublishTaskChanged(task);
            });

            await _downloadPipeline.DownloadAsync(task, progress, cts.Token).ConfigureAwait(false);

            if (cts.IsCancellationRequested || !_taskMap.ContainsKey(task.Sha256))
                throw new OperationCanceledException(cts.Token);

            task.State = TaskState.Verifying;
            task.StatusText = "Verifying SHA-256...";
            task.SpeedBytesPerSecond = 0;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);

            await _downloadPipeline.VerifyAsync(task, cts.Token).ConfigureAwait(false);

            task.State = TaskState.Completed;
            task.Progress = 1.0;
            task.StatusText = "Download completed";
            task.SpeedBytesPerSecond = 0;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            // App is shutting down; StartAsync resets in-flight tasks to Queued next time.
        }
        catch (OperationCanceledException)
        {
            if (_cancelledTasks.TryRemove(task.Sha256, out _))
                return;

            task.State = TaskState.Queued;
            task.StatusText = "Paused";
            task.SpeedBytesPerSecond = 0;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);
        }
        catch (InvalidDataException ex)
        {
            if (_cancelledTasks.TryRemove(task.Sha256, out _))
                return;

            task.State = TaskState.Failed;
            task.ErrorMessage = ex.Message;
            task.StatusText = "Verification failed";
            task.SpeedBytesPerSecond = 0;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);
        }
        catch (Exception ex)
        {
            if (_cancelledTasks.TryRemove(task.Sha256, out _))
                return;

            task.State = TaskState.Failed;
            task.ErrorMessage = ex.Message;
            task.StatusText = "Download failed";
            task.SpeedBytesPerSecond = 0;
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await _cache.UpdateTaskAsync(task).ConfigureAwait(false);
            PublishTaskChanged(task);
        }
        finally
        {
            if (cts is not null)
            {
                TryRemoveActiveCts(task.Sha256, cts);
                cts.Dispose();
            }

            if (acquiredSlot)
                ReleaseDownloadSlot();

            if (countedActive)
                DecrementActiveTaskCount();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        >= 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    private static string BuildDuplicateTaskMessage(DownloadTask task) => task.State switch
    {
        TaskState.Queued => "The file is already queued for download.",
        TaskState.Downloading => "The file is currently downloading.",
        TaskState.Verifying => "The file has been downloaded and is being verified.",
        TaskState.Completed => "The file has been downloaded. Please open the directory in the download tasks page.",
        TaskState.Failed => "A failed task for this file already exists. Please remove it from the download tasks page before adding it again.",
        _ => "The download task already exists.",
    };

    private async Task<bool> WaitForDownloadSlotAsync(DownloadTask task, CancellationToken shutdownToken)
    {
        while (!shutdownToken.IsCancellationRequested)
        {
            if (!_taskMap.ContainsKey(task.Sha256) || task.State != TaskState.Queued)
                return false;

            if (TryAcquireDownloadSlot())
                return true;

            await Task.Delay(_downloadSlotPollInterval, shutdownToken).ConfigureAwait(false);
        }

        return false;
    }

    private bool TryAcquireDownloadSlot()
    {
        while (true)
        {
            var maxConcurrentDownloads = _settings.MaxConcurrentDownloads;
            var current = Volatile.Read(ref _activeDownloadSlotCount);
            if (current >= maxConcurrentDownloads)
                return false;

            if (Interlocked.CompareExchange(ref _activeDownloadSlotCount, current + 1, current) == current)
                return true;
        }
    }

    private void ReleaseDownloadSlot() => Interlocked.Decrement(ref _activeDownloadSlotCount);

    private void IncrementActiveTaskCount()
    {
        Interlocked.Increment(ref _activeDownloadCount);
        ActiveTaskCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DecrementActiveTaskCount()
    {
        Interlocked.Decrement(ref _activeDownloadCount);
        ActiveTaskCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void PublishTaskChanged(DownloadTask task) =>
        TaskChanged?.Invoke(this, DownloadTaskSnapshot.FromTask(task));

    private static void RequestCancellation(CancellationTokenSource cts) =>
        _ = Task.Run(async () =>
        {
            try { await cts.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

    private void TryRemoveActiveCts(string sha256, CancellationTokenSource cts) =>
        ((ICollection<KeyValuePair<string, CancellationTokenSource>>)_activeCts)
        .Remove(new KeyValuePair<string, CancellationTokenSource>(sha256, cts));

    private void TryRemoveDownloadWorker(string sha256, Task worker) =>
        ((ICollection<KeyValuePair<string, Task>>)_downloadWorkers)
        .Remove(new KeyValuePair<string, Task>(sha256, worker));

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (!_shutdownCts.IsCancellationRequested)
            await StopAsync(CancellationToken.None).ConfigureAwait(false);

        _shutdownCts.Dispose();
    }
}
