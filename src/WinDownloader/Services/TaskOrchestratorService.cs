using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using WinDownloader.Interfaces;
using WinDownloader.Iso;
using WinDownloader.Iso.Interfaces;
using WinDownloader.Models;

namespace WinDownloader.Services;

/// <summary>
/// Implements <see cref="ITaskOrchestratorService"/> for ESD-only download tasks.
/// </summary>
public sealed class TaskOrchestratorService : ITaskOrchestratorService, IAsyncDisposable
{
    private readonly ICacheService _cache;
    private readonly IEsdDownloadPipeline _downloadPipeline;
    private readonly IEsdToIsoConversionService _isoConversionService;
    private readonly IDownloadTaskPathService _pathService;
    private readonly IAppSettings _settings;

    // ── In-memory task registry ───────────────────────────────────────────────
    private readonly ObservableCollection<DownloadTask> _tasks = [];
    private readonly ConcurrentDictionary<string, DownloadTask> _taskMap = new(StringComparer.OrdinalIgnoreCase);

    // ── Download workers ──────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _cancelledTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _downloadWorkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task> _isoConversionWorkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EsdToIsoTaskSnapshot> _isoSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly object _isoWorkerLock = new();
    private static readonly TimeSpan _downloadSlotPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan _conversionSlotPollInterval = TimeSpan.FromMilliseconds(250);
    private int _activeDownloadSlotCount;
    private int _activeDownloadCount;
    private int _activeIsoConversionCount;
    private bool _disposed;

    public TaskOrchestratorService(
        ICacheService cache,
        IEsdDownloadPipeline downloadPipeline,
        IEsdToIsoConversionService isoConversionService,
        IDownloadTaskPathService pathService,
        IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(downloadPipeline);
        ArgumentNullException.ThrowIfNull(isoConversionService);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(settings);

        _cache = cache;
        _downloadPipeline = downloadPipeline;
        _isoConversionService = isoConversionService;
        _pathService = pathService;
        _settings = settings;
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

        var workers = _downloadWorkers.Values.Concat(_isoConversionWorkers.Values).ToArray();
        if (workers.Length > 0)
            await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── ITaskOrchestratorService ──────────────────────────────────────────────

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
    public Task<TaskOperationResult> ConvertToIsoAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_taskMap.TryGetValue(sha256, out var task))
            return Task.FromResult(TaskOperationResult.Failure("Task not found, unable to convert to ISO."));

        if (task.State != TaskState.Completed)
            return Task.FromResult(TaskOperationResult.Failure($"Can only convert tasks that are completed, current state is {task.State}."));

        var esdPath = _pathService.ResolveEsdPath(task);
        if (!File.Exists(esdPath))
            return Task.FromResult(TaskOperationResult.Failure("Local ESD file not found, please re-download before converting."));

        if (File.Exists(_pathService.ResolveIsoPath(task)))
            return Task.FromResult(TaskOperationResult.Success("ISO file already exists."));

        lock (_isoWorkerLock)
        {
            if (_isoConversionWorkers.ContainsKey(task.Sha256))
                return Task.FromResult(TaskOperationResult.Failure("Task is already in the ISO conversion queue."));

            PublishIsoSnapshot(task, CreateIsoSnapshot(task, EsdToIsoTaskState.NotStarted, EsdToIsoStage.Preparing, 0));

            var worker = Task.Run(() => ProcessIsoConversionAsync(task, _shutdownCts.Token));
            _isoConversionWorkers[task.Sha256] = worker;
            _ = worker.ContinueWith(_ => TryRemoveIsoConversionWorker(task.Sha256, worker), TaskScheduler.Default);
        }

        return Task.FromResult(TaskOperationResult.Success("Task has been added to the ISO conversion queue."));
    }

    /// <inheritdoc/>
    public async Task<TaskOperationResult> DeleteAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);

        if (!_taskMap.TryGetValue(sha256, out var task))
            return TaskOperationResult.Failure("Task not found, unable to delete.");

        if (task.State != TaskState.Completed)
            return TaskOperationResult.Failure($"Can only delete tasks that are completed.");

        if (_isoConversionWorkers.ContainsKey(sha256))
            return TaskOperationResult.Failure("ISO conversion is not yet complete, please wait until it finishes before deleting the file.");

        TryDeleteFile(_pathService.ResolveEsdPath(task));
        _isoSnapshots.TryRemove(sha256, out _);

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

    // ── ISO conversion worker ────────────────────────────────────────────────

    private async Task ProcessIsoConversionAsync(DownloadTask task, CancellationToken shutdownToken)
    {
        var acquiredSlot = false;
        var countedActive = false;
        EventHandler<EsdToIsoTaskSnapshot>? progressHandler = null;

        try
        {
            acquiredSlot = await WaitForIsoConversionSlotAsync(task, shutdownToken).ConfigureAwait(false);
            if (!acquiredSlot || !_taskMap.ContainsKey(task.Sha256))
                return;

            countedActive = true;
            IncrementActiveTaskCount();

            var sourceEsdPath = _pathService.ResolveEsdPath(task);
            if (!File.Exists(sourceEsdPath))
            {
                PublishIsoSnapshot(task, CreateIsoSnapshot(
                    task,
                    EsdToIsoTaskState.Failed,
                    EsdToIsoStage.Failed,
                    0,
                    errorMessage: "Local ESD file does not exist, please re-download and try converting again.",
                    completedAt: DateTimeOffset.Now));
                return;
            }

            if (File.Exists(_pathService.ResolveIsoPath(task)))
            {
                PublishIsoSnapshot(task, CreateIsoSnapshot(
                    task,
                    EsdToIsoTaskState.Completed,
                    EsdToIsoStage.Completed,
                    1,
                    currentFile: _pathService.ResolveIsoPath(task),
                    completedAt: DateTimeOffset.Now));
                return;
            }

            progressHandler = (_, snapshot) =>
            {
                if (string.Equals(snapshot.SourceEsdPath, sourceEsdPath, StringComparison.OrdinalIgnoreCase))
                    PublishIsoSnapshot(task, snapshot);
            };
            _isoConversionService.ProgressChanged += progressHandler;

            var request = new EsdToIsoRequest(
                sourceEsdPath,
                _pathService.ResolveIsoStagingDirectory(task),
                BuildIsoVolumeLabel(task),
                KeepIntermediateFiles: false);

            var result = await _isoConversionService.ConvertAsync(request, shutdownToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                PublishIsoSnapshot(task, CreateIsoSnapshot(
                    task,
                    EsdToIsoTaskState.Failed,
                    EsdToIsoStage.Failed,
                    _isoSnapshots.TryGetValue(task.Sha256, out var lastSnapshot) ? lastSnapshot.Progress : 0,
                    errorMessage: result.ErrorMessage ?? "ISO conversion failed.",
                    completedAt: result.CompletedAt));
            }
        }
        catch (OperationCanceledException) when (shutdownToken.IsCancellationRequested)
        {
            PublishIsoSnapshot(task, CreateIsoSnapshot(
                task,
                EsdToIsoTaskState.Canceled,
                EsdToIsoStage.Failed,
                _isoSnapshots.TryGetValue(task.Sha256, out var lastSnapshot) ? lastSnapshot.Progress : 0,
                errorMessage: "ISO conversion has been canceled.",
                completedAt: DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            PublishIsoSnapshot(task, CreateIsoSnapshot(
                task,
                EsdToIsoTaskState.Failed,
                EsdToIsoStage.Failed,
                _isoSnapshots.TryGetValue(task.Sha256, out var lastSnapshot) ? lastSnapshot.Progress : 0,
                errorMessage: ex.Message,
                completedAt: DateTimeOffset.Now));
        }
        finally
        {
            if (progressHandler is not null)
                _isoConversionService.ProgressChanged -= progressHandler;

            if (acquiredSlot)
                ReleaseIsoConversionSlot();

            if (countedActive)
                DecrementActiveTaskCount();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:F1} MB",
        >= 1024        => $"{bytes / 1024.0:F1} KB",
        _              => $"{bytes} B",
    };

    private static string BuildDuplicateTaskMessage(DownloadTask task) => task.State switch
    {
        TaskState.Queued      => "The file is already queued for download.",
        TaskState.Downloading => "The file is currently downloading.",
        TaskState.Verifying   => "The file has been downloaded and is being verified.",
        TaskState.Completed   => "The file has been downloaded. Please open the directory in the download tasks page.",
        TaskState.Failed      => "A failed task for this file already exists. Please remove it from the download tasks page before adding it again.",
        _                     => "The download task already exists.",
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

    private async Task<bool> WaitForIsoConversionSlotAsync(DownloadTask task, CancellationToken shutdownToken)
    {
        while (!shutdownToken.IsCancellationRequested)
        {
            if (!_taskMap.ContainsKey(task.Sha256) || task.State != TaskState.Completed)
                return false;

            if (TryAcquireIsoConversionSlot())
                return true;

            await Task.Delay(_conversionSlotPollInterval, shutdownToken).ConfigureAwait(false);
        }

        return false;
    }

    private bool TryAcquireIsoConversionSlot()
    {
        while (true)
        {
            var current = Volatile.Read(ref _activeIsoConversionCount);
            if (current >= 1)
                return false;

            if (Interlocked.CompareExchange(ref _activeIsoConversionCount, current + 1, current) == current)
                return true;
        }
    }

    private void ReleaseIsoConversionSlot() => Interlocked.Decrement(ref _activeIsoConversionCount);

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

    private void PublishTaskChanged(DownloadTask task)
    {
        _isoSnapshots.TryGetValue(task.Sha256, out var isoSnapshot);
        TaskChanged?.Invoke(this, DownloadTaskSnapshot.FromTask(task, isoSnapshot));
    }

    private void PublishIsoSnapshot(DownloadTask task, EsdToIsoTaskSnapshot snapshot)
    {
        _isoSnapshots[task.Sha256] = snapshot;
        PublishTaskChanged(task);
    }

    private EsdToIsoTaskSnapshot CreateIsoSnapshot(
        DownloadTask task,
        EsdToIsoTaskState state,
        EsdToIsoStage stage,
        double progress,
        string? currentFile = null,
        string? errorMessage = null,
        DateTimeOffset? completedAt = null)
    {
        var startedAt = _isoSnapshots.TryGetValue(task.Sha256, out var existing)
            ? existing.StartedAt
            : DateTimeOffset.Now;

        return new EsdToIsoTaskSnapshot(
            Path.GetFileNameWithoutExtension(task.FileGroup.File.FileName),
            _pathService.ResolveEsdPath(task),
            state,
            stage,
            Math.Clamp(progress, 0, 1),
            currentFile,
            errorMessage,
            _pathService.ResolveIsoPath(task),
            startedAt,
            completedAt,
            DateTimeOffset.Now - startedAt);
    }

    private static string BuildIsoVolumeLabel(DownloadTask task)
    {
        var source = Path.GetFileNameWithoutExtension(task.FileGroup.File.FileName);
        var characters = source.Select(static character =>
            char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_').ToArray();
        var label = new string(characters).Trim('_');
        if (string.IsNullOrWhiteSpace(label))
            label = "ESD_ISO";

        return label.Length <= 32 ? label : label[..32];
    }

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

    private void TryRemoveIsoConversionWorker(string sha256, Task worker) =>
        ((ICollection<KeyValuePair<string, Task>>)_isoConversionWorkers)
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
