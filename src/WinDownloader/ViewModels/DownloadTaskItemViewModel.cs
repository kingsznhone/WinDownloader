// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinDownloader.Helpers;
using WinDownloader.Interfaces;
using WinDownloader.Models;
using WinDownloader.Services;
using WinDownloader.Wim.Models;

namespace WinDownloader.ViewModels;

/// <summary>
/// Wraps a <see cref="DownloadTask"/> and exposes commands and computed properties
/// for the <c>DownloadTaskItemControl</c> UI.
/// </summary>
public sealed partial class DownloadTaskItemViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadTaskOrchestratorService _downloadOrchestrator;
    private readonly IEsdToIsoOrchestratorService _isoOrchestrator;
    private readonly IDownloadTaskPathService _pathService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Lock _downloadSnapshotLock = new();
    private readonly Lock _isoSnapshotLock = new();
    private DownloadTaskSnapshot? _pendingDownloadSnapshot;
    private EsdToIsoTaskSnapshot? _pendingIsoSnapshot;
    private bool _downloadSnapshotRefreshQueued;
    private bool _isoSnapshotRefreshQueued;
    private TaskState _state;
    private double _progress;
    private long _speedBytesPerSecond;
    private EsdToIsoTaskSnapshot? _isoSnapshot;
    private double _isoMainProgress;
    private double _isoSubProgress;

    public DownloadTaskItemViewModel(
        DownloadTask task,
        IDownloadTaskOrchestratorService downloadOrchestrator,
        IEsdToIsoOrchestratorService isoOrchestrator,
        IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(downloadOrchestrator);
        ArgumentNullException.ThrowIfNull(isoOrchestrator);
        ArgumentNullException.ThrowIfNull(pathService);

        Task = task;
        _downloadOrchestrator = downloadOrchestrator;
        _isoOrchestrator = isoOrchestrator;
        _pathService = pathService;
        // Must be captured on the UI thread (constructor is always called from UI thread via DI).
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ApplySnapshot(DownloadTaskSnapshot.FromTask(task), notify: false);
        ApplyIsoSnapshot(_isoOrchestrator.GetSnapshot(task.Sha256), notify: false);
        _downloadOrchestrator.TaskChanged += OnTaskChanged;
        _isoOrchestrator.ConversionChanged += OnIsoConversionChanged;
    }

    public DownloadTask Task { get; }

    public double Progress => double.IsFinite(_progress) ? _progress : 0;
    public string StatusText { get; private set; } = string.Empty;
    public string ErrorMessage { get; private set; } = string.Empty;
    public double IsoMainProgress => double.IsFinite(_isoMainProgress) ? _isoMainProgress : 0;
    public double IsoSubProgress => double.IsFinite(_isoSubProgress) ? _isoSubProgress : 0;
    public bool IsIsoSubProgressIndeterminate { get; private set; }
    public string IsoMainStatusText { get; private set; } = string.Empty;
    public string IsoSubStatusText { get; private set; } = string.Empty;

    public string OperationMessage
    {
        get; private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasOperationMessage));
            }
        }
    } = string.Empty;

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        TaskOperationResult result = await _downloadOrchestrator.PauseAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        TaskOperationResult result = await _downloadOrchestrator.ResumeAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        TaskOperationResult result = await _downloadOrchestrator.CancelAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        TaskOperationResult result = await _downloadOrchestrator.DeleteAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private void OpenDirectory()
    {
        string directory = _pathService.ResolveDirectory(Task);
        OpenDirectoryPath(directory);
    }

    [RelayCommand(CanExecute = nameof(CanConvertToIso))]
    private async Task ConvertToIsoAsync()
    {
        string isoPath = _pathService.ResolveIsoPath(Task);
        if (File.Exists(isoPath))
        {
            OpenDirectoryPath(Path.GetDirectoryName(isoPath) ?? _pathService.ResolveDirectory(Task));
            return;
        }

        TaskOperationResult result = await _isoOrchestrator.ConvertToIsoAsync(Task);
        ApplyOperationResult(result);
    }

    private void OpenDirectoryPath(string directory)
    {
        if (!Directory.Exists(directory))
        {
            OperationMessage = StringRes.Get("DownloadTask_DirectoryNotFound");
            return;
        }

        OperationMessage = string.Empty;
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    // ── State flags ───────────────────────────────────────────────────────────

    public bool IsDownloading => _state == TaskState.Downloading;
    public bool IsVerifying => _state == TaskState.Verifying;
    public bool IsQueued => _state == TaskState.Queued;
    public bool IsCompleted => _state == TaskState.Completed;
    public bool IsFailed => _state == TaskState.Failed;
    public bool IsIsoConversionBusy => _isoSnapshot?.State is EsdToIsoTaskState.NotStarted or EsdToIsoTaskState.Running;

    /// <summary>True while the task has not yet reached a terminal state.</summary>
    public bool IsActive => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;

    public bool IsDownloadCompleted => _state == TaskState.Completed;

    public bool ShowDownloadProgress => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;
    public bool ShowIsoProgress => _isoSnapshot?.State is EsdToIsoTaskState.NotStarted or EsdToIsoTaskState.Running or EsdToIsoTaskState.Failed or EsdToIsoTaskState.Canceled;
    public bool ShowActionBar => IsDownloadCompleted;
    public bool HasStatusText => !string.IsNullOrEmpty(StatusText);
    public bool HasError => IsFailed && !string.IsNullOrEmpty(ErrorMessage);
    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);
    public bool HasIsoSubStatusText => !string.IsNullOrWhiteSpace(IsoSubStatusText);

    public bool CanPause => _state == TaskState.Downloading;
    public bool CanResume => _state == TaskState.Queued;
    public bool CanCancel => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying or TaskState.Failed;
    public bool CanOpenDirectory => IsDownloadCompleted;
    public bool CanConvertToIso => IsDownloadCompleted && !IsIsoConversionBusy;
    public bool CanDelete => _state == TaskState.Completed && !IsIsoConversionBusy;
    public string CancelButtonText => IsFailed ? StringRes.Get("DownloadTask_CancelButtonRemove") : StringRes.Get("DownloadTask_CancelButtonCancel");
    public string ConvertToIsoButtonText => IsoFileExists ? StringRes.Get("DownloadTask_OpenIsoDirectory") : StringRes.Get("DownloadTask_ConvertToIso");

    // ── Display strings ───────────────────────────────────────────────────────

    /// <summary>Human-readable download speed string.</summary>
    public string SpeedText => _speedBytesPerSecond switch
    {
        <= 0 => string.Empty,
        < 1024 => $"{_speedBytesPerSecond} B/s",
        < 1024 * 1024 => $"{_speedBytesPerSecond / 1024.0:F1} KB/s",
        _ => $"{_speedBytesPerSecond / 1024.0 / 1024.0:F2} MB/s"
    };

    private void OnTaskChanged(object? sender, DownloadTaskSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Sha256, Task.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_downloadSnapshotLock)
        {
            _pendingDownloadSnapshot = snapshot;

            if (_downloadSnapshotRefreshQueued)
            {
                return;
            }

            _downloadSnapshotRefreshQueued = true;
        }

        if (!_dispatcherQueue.TryEnqueue(ApplyPendingSnapshot))
        {
            lock (_downloadSnapshotLock)
            {
                _downloadSnapshotRefreshQueued = false;
            }
        }
    }

    private void OnIsoConversionChanged(object? sender, IsoConversionTaskSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Sha256, Task.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_isoSnapshotLock)
        {
            _pendingIsoSnapshot = snapshot.Snapshot;

            if (_isoSnapshotRefreshQueued)
            {
                return;
            }

            _isoSnapshotRefreshQueued = true;
        }

        if (!_dispatcherQueue.TryEnqueue(ApplyPendingIsoSnapshot))
        {
            lock (_isoSnapshotLock)
            {
                _isoSnapshotRefreshQueued = false;
            }
        }
    }

    private void ApplyPendingSnapshot()
    {
        DownloadTaskSnapshot? snapshot;

        lock (_downloadSnapshotLock)
        {
            snapshot = _pendingDownloadSnapshot;
            _pendingDownloadSnapshot = null;
            _downloadSnapshotRefreshQueued = false;
        }

        if (snapshot is not null)
        {
            ApplySnapshot(snapshot, notify: true);
        }
    }

    private void ApplyPendingIsoSnapshot()
    {
        EsdToIsoTaskSnapshot? snapshot;

        lock (_isoSnapshotLock)
        {
            snapshot = _pendingIsoSnapshot;
            _pendingIsoSnapshot = null;
            _isoSnapshotRefreshQueued = false;
        }

        ApplyIsoSnapshot(snapshot, notify: true);
    }

    private void ApplySnapshot(DownloadTaskSnapshot snapshot, bool notify)
    {
        bool lifecycleChanged = _state != snapshot.State;
        bool progressChanged = !AreClose(_progress, snapshot.Progress);
        bool speedChanged = _speedBytesPerSecond != snapshot.SpeedBytesPerSecond;
        bool statusChanged = !string.Equals(StatusText, snapshot.StatusText, StringComparison.Ordinal);
        string errorMessage = snapshot.ErrorMessage ?? string.Empty;
        bool errorChanged = !string.Equals(ErrorMessage, errorMessage, StringComparison.Ordinal);

        _state = snapshot.State;
        _progress = snapshot.Progress;
        _speedBytesPerSecond = snapshot.SpeedBytesPerSecond;
        StatusText = snapshot.StatusText;
        ErrorMessage = errorMessage;

        if (!notify)
        {
            return;
        }

        if (lifecycleChanged)
        {
            NotifyLifecyclePropertiesChanged();
        }

        if (progressChanged)
        {
            OnPropertyChanged(nameof(Progress));
        }

        if (speedChanged)
        {
            OnPropertyChanged(nameof(SpeedText));
        }

        if (statusChanged)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(HasStatusText));
        }
        if (errorChanged)
        {
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(HasError));
        }
    }

    private static bool AreClose(double left, double right) =>
        Math.Abs(left - right) < 0.0001 || (double.IsNaN(left) && double.IsNaN(right));

    private void NotifyLifecyclePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsVerifying));
        OnPropertyChanged(nameof(IsQueued));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsIsoConversionBusy));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDownloadCompleted));
        OnPropertyChanged(nameof(ShowDownloadProgress));
        OnPropertyChanged(nameof(ShowIsoProgress));
        OnPropertyChanged(nameof(ShowActionBar));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanOpenDirectory));
        OnPropertyChanged(nameof(CanConvertToIso));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CancelButtonText));
        OnPropertyChanged(nameof(ConvertToIsoButtonText));
        NotifyCommandStatesChanged();
    }

    private void NotifyCommandStatesChanged()
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OpenDirectoryCommand.NotifyCanExecuteChanged();
        ConvertToIsoCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void ApplyIsoSnapshot(EsdToIsoTaskSnapshot? snapshot, bool notify)
    {
        bool snapshotChanged = !Equals(_isoSnapshot, snapshot);
        double mainProgress = snapshot?.Progress ?? 0;
        (double subProgress, bool isSubIndeterminate) = CalculateIsoSubProgress(snapshot);
        string mainStatusText = snapshot is null ? string.Empty : BuildIsoMainStatusText(snapshot);
        string subStatusText = snapshot is null ? string.Empty : BuildIsoSubStatusText(snapshot);

        bool mainProgressChanged = !AreClose(_isoMainProgress, mainProgress);
        bool subProgressChanged = !AreClose(_isoSubProgress, subProgress);
        bool subIndeterminateChanged = IsIsoSubProgressIndeterminate != isSubIndeterminate;
        bool mainTextChanged = !string.Equals(IsoMainStatusText, mainStatusText, StringComparison.Ordinal);
        bool subTextChanged = !string.Equals(IsoSubStatusText, subStatusText, StringComparison.Ordinal);
        bool wasBusy = IsIsoConversionBusy;

        _isoSnapshot = snapshot;
        _isoMainProgress = mainProgress;
        _isoSubProgress = subProgress;
        IsIsoSubProgressIndeterminate = isSubIndeterminate;
        IsoMainStatusText = mainStatusText;
        IsoSubStatusText = subStatusText;

        if (!notify)
        {
            return;
        }

        if (snapshotChanged)
        {
            OnPropertyChanged(nameof(ShowIsoProgress));
        }

        if (mainProgressChanged)
        {
            OnPropertyChanged(nameof(IsoMainProgress));
        }

        if (subProgressChanged)
        {
            OnPropertyChanged(nameof(IsoSubProgress));
        }

        if (subIndeterminateChanged)
        {
            OnPropertyChanged(nameof(IsIsoSubProgressIndeterminate));
        }

        if (mainTextChanged)
        {
            OnPropertyChanged(nameof(IsoMainStatusText));
        }

        if (subTextChanged)
        {
            OnPropertyChanged(nameof(IsoSubStatusText));
            OnPropertyChanged(nameof(HasIsoSubStatusText));
        }

        if (wasBusy != IsIsoConversionBusy || snapshotChanged)
        {
            OnPropertyChanged(nameof(IsIsoConversionBusy));
            OnPropertyChanged(nameof(CanConvertToIso));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(ConvertToIsoButtonText));
            NotifyCommandStatesChanged();
        }
        else if (snapshot?.State == EsdToIsoTaskState.Completed)
        {
            OnPropertyChanged(nameof(ConvertToIsoButtonText));
        }
    }

    private bool IsoFileExists
    {
        get
        {
            try { return File.Exists(_pathService.ResolveIsoPath(Task)); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
    }

    private static (double Progress, bool IsIndeterminate) CalculateIsoSubProgress(EsdToIsoTaskSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return (0, false);
        }

        if (snapshot.IsoProgress is { } isoProgress)
        {
            return (Math.Clamp(isoProgress.Percent / 100d, 0, 1), false);
        }

        if (snapshot.WimProgress?.Percent is double wimPercent)
        {
            return (Math.Clamp(wimPercent / 100d, 0, 1), false);
        }

        if (snapshot.State is EsdToIsoTaskState.NotStarted or EsdToIsoTaskState.Running)
        {
            return (0, true);
        }

        return (snapshot.State == EsdToIsoTaskState.Completed ? 1 : 0, false);
    }

    private static string BuildIsoMainStatusText(EsdToIsoTaskSnapshot snapshot)
    {
        string stageText = snapshot.State switch
        {
            EsdToIsoTaskState.NotStarted => StringRes.Get("IsoState_NotStarted"),
            EsdToIsoTaskState.Completed => StringRes.Get("IsoState_Completed"),
            EsdToIsoTaskState.Failed => StringRes.Get("IsoState_Failed"),
            EsdToIsoTaskState.Canceled => StringRes.Get("IsoState_Canceled"),
            _ => snapshot.Stage switch
            {
                EsdToIsoStage.Preparing => StringRes.Get("IsoStage_Preparing"),
                EsdToIsoStage.InspectingSource => StringRes.Get("IsoStage_InspectingSource"),
                EsdToIsoStage.ApplyingSetupMedia => StringRes.Get("IsoStage_ApplyingSetupMedia"),
                EsdToIsoStage.BuildingBootWim => StringRes.Get("IsoStage_BuildingBootWim"),
                EsdToIsoStage.BuildingInstallImage => StringRes.Get("IsoStage_BuildingInstallImage"),
                EsdToIsoStage.CreatingIso => StringRes.Get("IsoStage_CreatingIso"),
                _ => StringRes.Get("IsoStage_Default")
            }
        };

        return $"{stageText} - {snapshot.Progress * 100d:0.0}%";
    }

    private static string BuildIsoSubStatusText(EsdToIsoTaskSnapshot snapshot)
    {
        if (snapshot.State is EsdToIsoTaskState.Failed or EsdToIsoTaskState.Canceled)
        {
            return snapshot.ErrorMessage ?? StringRes.Get("IsoSub_ConversionIncomplete");
        }

        if (snapshot.WimProgress is { } wimProgress)
        {
            return BuildWimProgressText(wimProgress);
        }

        if (snapshot.IsoProgress is { } isoProgress)
        {
            return string.Format(StringRes.Get("IsoSub_OscdimgWritingFormat"), isoProgress.Percent);
        }

        return snapshot.State switch
        {
            EsdToIsoTaskState.NotStarted => StringRes.Get("IsoSub_WaitingSlot"),
            EsdToIsoTaskState.Completed => string.IsNullOrWhiteSpace(snapshot.IsoPath)
                ? StringRes.Get("IsoSub_IsoGenerated")
                : string.Format(StringRes.Get("IsoSub_OutputFileFormat"), Path.GetFileName(snapshot.IsoPath)),
            _ => snapshot.Stage switch
            {
                EsdToIsoStage.Preparing => StringRes.Get("IsoSub_Stage_Preparing"),
                EsdToIsoStage.InspectingSource => StringRes.Get("IsoSub_Stage_InspectingSource"),
                EsdToIsoStage.ApplyingSetupMedia => StringRes.Get("IsoSub_Stage_ApplyingSetupMedia"),
                EsdToIsoStage.BuildingBootWim => StringRes.Get("IsoSub_Stage_BuildingBootWim"),
                EsdToIsoStage.BuildingInstallImage => StringRes.Get("IsoSub_Stage_BuildingInstallImage"),
                EsdToIsoStage.CreatingIso => StringRes.Get("IsoSub_Stage_CreatingIso"),
                _ => string.Empty
            }
        };
    }

    private static string BuildWimProgressText(WimOperationProgress progress)
    {
        string percentText = progress.Percent is double percent ? $" {percent:0.0}%" : string.Empty;
        string itemText = string.IsNullOrWhiteSpace(progress.CurrentItem)
            ? string.Empty
            : $" - {Path.GetFileName(progress.CurrentItem)}";

        return progress.Stage switch
        {
            WimOperationStage.Extracting => $"{StringRes.Get("WimStage_Extracting")}{percentText}{itemText}",
            WimOperationStage.Writing => $"{StringRes.Get("WimStage_Writing")}{percentText}{itemText}",
            WimOperationStage.Verifying => $"{StringRes.Get("WimStage_Verifying")}{percentText}{itemText}",
            WimOperationStage.Metadata => $"{StringRes.Get("WimStage_Metadata")}{itemText}",
            WimOperationStage.Completed => $"{StringRes.Get("WimStage_Completed")}{itemText}",
            _ => $"{StringRes.Get("WimStage_Default")}{percentText}{itemText}"
        };
    }

    private void ApplyOperationResult(TaskOperationResult result)
    {
        OperationMessage = result.Succeeded
            ? string.Empty
            : result.Message ?? StringRes.Get("DownloadTask_OperationFailed");
    }

    public void Dispose()
    {
        _downloadOrchestrator.TaskChanged -= OnTaskChanged;
        _isoOrchestrator.ConversionChanged -= OnIsoConversionChanged;
    }
}
