// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using WinDownloader.Interfaces;
using WinDownloader.Models;

namespace WinDownloader.Services;

/// <summary>
/// Orchestrates manual ESD to ISO conversion workers.
/// </summary>
public sealed class EsdToIsoOrchestratorService : IEsdToIsoOrchestratorService, IAsyncDisposable
{
    private readonly IEsdToIsoConversionService _isoConversionService;
    private readonly IDownloadTaskPathService _pathService;
    private readonly ConcurrentDictionary<string, Task> _conversionWorkers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, EsdToIsoTaskSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Lock _workerLock = new();
    private static readonly TimeSpan _conversionSlotPollInterval = TimeSpan.FromMilliseconds(250);
    private const int MaxConcurrentIsoConversions = 1;
    private int _activeConversionSlotCount;
    private int _activeTaskCount;
    private bool _disposed;

    public EsdToIsoOrchestratorService(
        IEsdToIsoConversionService isoConversionService,
        IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(isoConversionService);
        ArgumentNullException.ThrowIfNull(pathService);

        _isoConversionService = isoConversionService;
        _pathService = pathService;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct)
    {
        await _shutdownCts.CancelAsync().ConfigureAwait(false);

        Task[] workers = [.. _conversionWorkers.Values];
        if (workers.Length > 0)
        {
            await Task.WhenAll(workers).WaitAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public int ActiveTaskCount => Volatile.Read(ref _activeTaskCount);

    /// <inheritdoc/>
    public event EventHandler<IsoConversionTaskSnapshot>? ConversionChanged;

    /// <inheritdoc/>
    public event EventHandler? ActiveTaskCountChanged;

    /// <inheritdoc/>
    public Task<TaskOperationResult> ConvertToIsoAsync(DownloadTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ct.ThrowIfCancellationRequested();

        if (task.State != TaskState.Completed)
        {
            return Task.FromResult(TaskOperationResult.Failure($"Can only convert tasks that are completed, current state is {task.State}."));
        }

        string esdPath = _pathService.ResolveEsdPath(task);
        if (!File.Exists(esdPath))
        {
            return Task.FromResult(TaskOperationResult.Failure("Local ESD file not found, please re-download before converting."));
        }

        if (File.Exists(_pathService.ResolveIsoPath(task)))
        {
            return Task.FromResult(TaskOperationResult.Success("ISO file already exists."));
        }

        lock (_workerLock)
        {
            if (_conversionWorkers.ContainsKey(task.Sha256))
            {
                return Task.FromResult(TaskOperationResult.Failure("Task is already in the ISO conversion queue."));
            }

            PublishSnapshot(task, CreateSnapshot(task, EsdToIsoTaskState.NotStarted, EsdToIsoStage.Preparing, 0));

            var worker = Task.Run(() => ProcessConversionAsync(task, _shutdownCts.Token), _shutdownCts.Token);
            _conversionWorkers[task.Sha256] = worker;
            _ = worker.ContinueWith(_ => TryRemoveWorker(task.Sha256, worker), TaskScheduler.Default);
        }

        return Task.FromResult(TaskOperationResult.Success("Task has been added to the ISO conversion queue."));
    }

    /// <inheritdoc/>
    public bool IsConversionQueuedOrRunning(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        return _conversionWorkers.ContainsKey(sha256);
    }

    /// <inheritdoc/>
    public EsdToIsoTaskSnapshot? GetSnapshot(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        return _snapshots.TryGetValue(sha256, out EsdToIsoTaskSnapshot? snapshot) ? snapshot : null;
    }

    /// <inheritdoc/>
    public void ClearSnapshot(string sha256)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        _snapshots.TryRemove(sha256, out _);
    }

    private async Task ProcessConversionAsync(DownloadTask task, CancellationToken ct)
    {
        bool acquiredSlot = false;
        bool countedActive = false;
        EventHandler<EsdToIsoTaskSnapshot>? progressHandler = null;

        try
        {
            acquiredSlot = await WaitForConversionSlotAsync(task, ct).ConfigureAwait(false);
            if (!acquiredSlot || task.State != TaskState.Completed)
            {
                return;
            }

            countedActive = true;
            IncrementActiveTaskCount();

            string sourceEsdPath = _pathService.ResolveEsdPath(task);
            if (!File.Exists(sourceEsdPath))
            {
                PublishSnapshot(task, CreateSnapshot(
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
                PublishSnapshot(task, CreateSnapshot(
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
                {
                    PublishSnapshot(task, snapshot);
                }
            };
            _isoConversionService.ProgressChanged += progressHandler;

            var request = new EsdToIsoRequest(
                sourceEsdPath,
                _pathService.ResolveIsoStagingDirectory(task),
                BuildIsoVolumeLabel(task),
                KeepIntermediateFiles: false);

            EsdToIsoResult result = await _isoConversionService.ConvertAsync(request, ct).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                PublishSnapshot(task, CreateSnapshot(
                    task,
                    EsdToIsoTaskState.Failed,
                    EsdToIsoStage.Failed,
                    _snapshots.TryGetValue(task.Sha256, out EsdToIsoTaskSnapshot? lastSnapshot) ? lastSnapshot.Progress : 0,
                    errorMessage: result.ErrorMessage ?? "ISO conversion failed.",
                    completedAt: result.CompletedAt));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            PublishSnapshot(task, CreateSnapshot(
                task,
                EsdToIsoTaskState.Canceled,
                EsdToIsoStage.Failed,
                _snapshots.TryGetValue(task.Sha256, out EsdToIsoTaskSnapshot? lastSnapshot) ? lastSnapshot.Progress : 0,
                errorMessage: "ISO conversion has been canceled.",
                completedAt: DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            PublishSnapshot(task, CreateSnapshot(
                task,
                EsdToIsoTaskState.Failed,
                EsdToIsoStage.Failed,
                _snapshots.TryGetValue(task.Sha256, out EsdToIsoTaskSnapshot? lastSnapshot) ? lastSnapshot.Progress : 0,
                errorMessage: ex.Message,
                completedAt: DateTimeOffset.Now));
        }
        finally
        {
            if (progressHandler is not null)
            {
                _isoConversionService.ProgressChanged -= progressHandler;
            }

            if (acquiredSlot)
            {
                ReleaseConversionSlot();
            }

            if (countedActive)
            {
                DecrementActiveTaskCount();
            }
        }
    }

    private async Task<bool> WaitForConversionSlotAsync(DownloadTask task, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (task.State != TaskState.Completed)
            {
                return false;
            }

            if (TryAcquireConversionSlot())
            {
                return true;
            }

            await Task.Delay(_conversionSlotPollInterval, ct).ConfigureAwait(false);
        }

        return false;
    }

    private bool TryAcquireConversionSlot()
    {
        while (true)
        {
            int current = Volatile.Read(ref _activeConversionSlotCount);
            if (current >= MaxConcurrentIsoConversions)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _activeConversionSlotCount, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    private void ReleaseConversionSlot() => Interlocked.Decrement(ref _activeConversionSlotCount);

    private void IncrementActiveTaskCount()
    {
        Interlocked.Increment(ref _activeTaskCount);
        ActiveTaskCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DecrementActiveTaskCount()
    {
        Interlocked.Decrement(ref _activeTaskCount);
        ActiveTaskCountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishSnapshot(DownloadTask task, EsdToIsoTaskSnapshot snapshot)
    {
        _snapshots[task.Sha256] = snapshot;
        ConversionChanged?.Invoke(this, new IsoConversionTaskSnapshot(task.Sha256, snapshot));
    }

    private EsdToIsoTaskSnapshot CreateSnapshot(
        DownloadTask task,
        EsdToIsoTaskState state,
        EsdToIsoStage stage,
        double progress,
        string? currentFile = null,
        string? errorMessage = null,
        DateTimeOffset? completedAt = null)
    {
        DateTimeOffset startedAt = _snapshots.TryGetValue(task.Sha256, out EsdToIsoTaskSnapshot? existing)
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
        string source = Path.GetFileNameWithoutExtension(task.FileGroup.File.FileName);
        char[] characters = [.. source.Select(static character =>
            char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')];
        string label = new string(characters).Trim('_');
        if (string.IsNullOrWhiteSpace(label))
        {
            label = "ESD_ISO";
        }

        return label.Length <= 32 ? label : label[..32];
    }

    private void TryRemoveWorker(string sha256, Task worker) =>
        ((ICollection<KeyValuePair<string, Task>>)_conversionWorkers)
        .Remove(new KeyValuePair<string, Task>(sha256, worker));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_shutdownCts.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _shutdownCts.Dispose();
    }
}
