using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinDownloader.Helpers;
using WinDownloader.Interfaces;
using WinDownloader.Iso;
using WinDownloader.Models;
using WinDownloader.Services;
using WinDownloader.Wim;

namespace WinDownloader.ViewModels;

/// <summary>
/// Wraps a <see cref="DownloadTask"/> and exposes commands and computed properties
/// for the <c>DownloadTaskItemControl</c> UI.
/// </summary>
public sealed partial class DownloadTaskItemViewModel : ObservableObject, IDisposable
{
    private readonly ITaskOrchestratorService _orchestrator;
    private readonly IDownloadTaskPathService _pathService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly object _snapshotLock = new();
    private string _operationMessage = string.Empty;
    private DownloadTaskSnapshot? _pendingSnapshot;
    private bool _snapshotRefreshQueued;
    private TaskState _state;
    private double _progress;
    private long _speedBytesPerSecond;
    private string _statusText = string.Empty;
    private string _errorMessage = string.Empty;
    private EsdToIsoTaskSnapshot? _isoSnapshot;
    private double _isoMainProgress;
    private double _isoSubProgress;
    private bool _isIsoSubProgressIndeterminate;
    private string _isoMainStatusText = string.Empty;
    private string _isoSubStatusText = string.Empty;

    public DownloadTaskItemViewModel(
        DownloadTask task,
        ITaskOrchestratorService orchestrator,
        IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(pathService);

        Task = task;
        _orchestrator = orchestrator;
        _pathService = pathService;
        // Must be captured on the UI thread (constructor is always called from UI thread via DI).
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ApplySnapshot(DownloadTaskSnapshot.FromTask(task), notify: false);
        _orchestrator.TaskChanged += OnTaskChanged;
    }

    public DownloadTask Task { get; }

    public double Progress => double.IsFinite(_progress) ? _progress : 0;
    public string StatusText => _statusText;
    public string ErrorMessage => _errorMessage;
    public double IsoMainProgress => double.IsFinite(_isoMainProgress) ? _isoMainProgress : 0;
    public double IsoSubProgress => double.IsFinite(_isoSubProgress) ? _isoSubProgress : 0;
    public bool IsIsoSubProgressIndeterminate => _isIsoSubProgressIndeterminate;
    public string IsoMainStatusText => _isoMainStatusText;
    public string IsoSubStatusText => _isoSubStatusText;

    public string OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetProperty(ref _operationMessage, value))
                OnPropertyChanged(nameof(HasOperationMessage));
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPause))]
    private async Task PauseAsync()
    {
        var result = await _orchestrator.PauseAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async Task ResumeAsync()
    {
        var result = await _orchestrator.ResumeAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        var result = await _orchestrator.CancelAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var result = await _orchestrator.DeleteAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private void OpenDirectory()
    {
        var directory = _pathService.ResolveDirectory(Task);
        OpenDirectoryPath(directory);
    }

    [RelayCommand(CanExecute = nameof(CanConvertToIso))]
    private async Task ConvertToIsoAsync()
    {
        var isoPath = _pathService.ResolveIsoPath(Task);
        if (File.Exists(isoPath))
        {
            OpenDirectoryPath(Path.GetDirectoryName(isoPath) ?? _pathService.ResolveDirectory(Task));
            return;
        }

        var result = await _orchestrator.ConvertToIsoAsync(Task.Sha256);
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
    public bool IsVerifying   => _state == TaskState.Verifying;
    public bool IsQueued      => _state == TaskState.Queued;
    public bool IsCompleted   => _state == TaskState.Completed;
    public bool IsFailed      => _state == TaskState.Failed;
    public bool IsIsoConversionBusy => _isoSnapshot?.State is EsdToIsoTaskState.NotStarted or EsdToIsoTaskState.Running;

    /// <summary>True while the task has not yet reached a terminal state.</summary>
    public bool IsActive => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;

    public bool IsDownloadCompleted => _state == TaskState.Completed;

    public bool ShowDownloadProgress => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;
    public bool ShowIsoProgress => _isoSnapshot is not null;
    public bool ShowActionBar => IsDownloadCompleted;
    public bool HasStatusText => !string.IsNullOrEmpty(_statusText);
    public bool HasError => IsFailed && !string.IsNullOrEmpty(_errorMessage);
    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);
    public bool HasIsoSubStatusText => !string.IsNullOrWhiteSpace(_isoSubStatusText);

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
            return;

        lock (_snapshotLock)
        {
            _pendingSnapshot = snapshot;

            if (_snapshotRefreshQueued)
                return;

            _snapshotRefreshQueued = true;
        }

        if (!_dispatcherQueue.TryEnqueue(ApplyPendingSnapshot))
        {
            lock (_snapshotLock)
            {
                _snapshotRefreshQueued = false;
            }
        }
    }

    private void ApplyPendingSnapshot()
    {
        DownloadTaskSnapshot? snapshot;

        lock (_snapshotLock)
        {
            snapshot = _pendingSnapshot;
            _pendingSnapshot = null;
            _snapshotRefreshQueued = false;
        }

        if (snapshot is not null)
            ApplySnapshot(snapshot, notify: true);
    }

    private void ApplySnapshot(DownloadTaskSnapshot snapshot, bool notify)
    {
        var lifecycleChanged = _state != snapshot.State;
        var progressChanged = !AreClose(_progress, snapshot.Progress);
        var speedChanged = _speedBytesPerSecond != snapshot.SpeedBytesPerSecond;
        var statusChanged = !string.Equals(_statusText, snapshot.StatusText, StringComparison.Ordinal);
        var errorMessage = snapshot.ErrorMessage ?? string.Empty;
        var errorChanged = !string.Equals(_errorMessage, errorMessage, StringComparison.Ordinal);

        _state = snapshot.State;
        _progress = snapshot.Progress;
        _speedBytesPerSecond = snapshot.SpeedBytesPerSecond;
        _statusText = snapshot.StatusText;
        _errorMessage = errorMessage;
        ApplyIsoSnapshot(snapshot.IsoConversionSnapshot, notify);

        if (!notify)
            return;

        if (lifecycleChanged)
            NotifyLifecyclePropertiesChanged();
        if (progressChanged)
            OnPropertyChanged(nameof(Progress));
        if (speedChanged)
            OnPropertyChanged(nameof(SpeedText));
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
        var snapshotChanged = !Equals(_isoSnapshot, snapshot);
        var mainProgress = snapshot?.Progress ?? 0;
        var (subProgress, isSubIndeterminate) = CalculateIsoSubProgress(snapshot);
        var mainStatusText = snapshot is null ? string.Empty : BuildIsoMainStatusText(snapshot);
        var subStatusText = snapshot is null ? string.Empty : BuildIsoSubStatusText(snapshot);

        var mainProgressChanged = !AreClose(_isoMainProgress, mainProgress);
        var subProgressChanged = !AreClose(_isoSubProgress, subProgress);
        var subIndeterminateChanged = _isIsoSubProgressIndeterminate != isSubIndeterminate;
        var mainTextChanged = !string.Equals(_isoMainStatusText, mainStatusText, StringComparison.Ordinal);
        var subTextChanged = !string.Equals(_isoSubStatusText, subStatusText, StringComparison.Ordinal);
        var wasBusy = IsIsoConversionBusy;

        _isoSnapshot = snapshot;
        _isoMainProgress = mainProgress;
        _isoSubProgress = subProgress;
        _isIsoSubProgressIndeterminate = isSubIndeterminate;
        _isoMainStatusText = mainStatusText;
        _isoSubStatusText = subStatusText;

        if (!notify)
            return;

        if (snapshotChanged)
            OnPropertyChanged(nameof(ShowIsoProgress));
        if (mainProgressChanged)
            OnPropertyChanged(nameof(IsoMainProgress));
        if (subProgressChanged)
            OnPropertyChanged(nameof(IsoSubProgress));
        if (subIndeterminateChanged)
            OnPropertyChanged(nameof(IsIsoSubProgressIndeterminate));
        if (mainTextChanged)
            OnPropertyChanged(nameof(IsoMainStatusText));
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
            return (0, false);

        if (snapshot.IsoProgress is { } isoProgress)
            return (Math.Clamp(isoProgress.Percent / 100d, 0, 1), false);

        if (snapshot.WimProgress?.Percent is double wimPercent)
            return (Math.Clamp(wimPercent / 100d, 0, 1), false);

        if (snapshot.State is EsdToIsoTaskState.NotStarted or EsdToIsoTaskState.Running)
            return (0, true);

        return (snapshot.State == EsdToIsoTaskState.Completed ? 1 : 0, false);
    }

    private static string BuildIsoMainStatusText(EsdToIsoTaskSnapshot snapshot)
    {
        var stageText = snapshot.State switch
        {
            EsdToIsoTaskState.NotStarted => StringRes.Get("IsoState_NotStarted"),
            EsdToIsoTaskState.Completed  => StringRes.Get("IsoState_Completed"),
            EsdToIsoTaskState.Failed     => StringRes.Get("IsoState_Failed"),
            EsdToIsoTaskState.Canceled   => StringRes.Get("IsoState_Canceled"),
            _ => snapshot.Stage switch
            {
                EsdToIsoStage.Preparing           => StringRes.Get("IsoStage_Preparing"),
                EsdToIsoStage.InspectingSource    => StringRes.Get("IsoStage_InspectingSource"),
                EsdToIsoStage.ApplyingSetupMedia  => StringRes.Get("IsoStage_ApplyingSetupMedia"),
                EsdToIsoStage.BuildingBootWim     => StringRes.Get("IsoStage_BuildingBootWim"),
                EsdToIsoStage.BuildingInstallImage => StringRes.Get("IsoStage_BuildingInstallImage"),
                EsdToIsoStage.CreatingIso         => StringRes.Get("IsoStage_CreatingIso"),
                _                                 => StringRes.Get("IsoStage_Default")
            }
        };

        return $"{stageText} - {snapshot.Progress * 100d:0.0}%";
    }

    private static string BuildIsoSubStatusText(EsdToIsoTaskSnapshot snapshot)
    {
        if (snapshot.State is EsdToIsoTaskState.Failed or EsdToIsoTaskState.Canceled)
            return snapshot.ErrorMessage ?? StringRes.Get("IsoSub_ConversionIncomplete");

        if (snapshot.WimProgress is { } wimProgress)
            return BuildWimProgressText(wimProgress);

        if (snapshot.IsoProgress is { } isoProgress)
            return string.Format(StringRes.Get("IsoSub_OscdimgWritingFormat"), isoProgress.Percent);

        return snapshot.State switch
        {
            EsdToIsoTaskState.NotStarted => StringRes.Get("IsoSub_WaitingSlot"),
            EsdToIsoTaskState.Completed => string.IsNullOrWhiteSpace(snapshot.IsoPath)
                ? StringRes.Get("IsoSub_IsoGenerated")
                : string.Format(StringRes.Get("IsoSub_OutputFileFormat"), Path.GetFileName(snapshot.IsoPath)),
            _ => snapshot.Stage switch
            {
                EsdToIsoStage.Preparing           => StringRes.Get("IsoSub_Stage_Preparing"),
                EsdToIsoStage.InspectingSource    => StringRes.Get("IsoSub_Stage_InspectingSource"),
                EsdToIsoStage.ApplyingSetupMedia  => StringRes.Get("IsoSub_Stage_ApplyingSetupMedia"),
                EsdToIsoStage.BuildingBootWim     => StringRes.Get("IsoSub_Stage_BuildingBootWim"),
                EsdToIsoStage.BuildingInstallImage => StringRes.Get("IsoSub_Stage_BuildingInstallImage"),
                EsdToIsoStage.CreatingIso         => StringRes.Get("IsoSub_Stage_CreatingIso"),
                _                                 => string.Empty
            }
        };
    }

    private static string BuildWimProgressText(WimOperationProgress progress)
    {
        var percentText = progress.Percent is double percent ? $" {percent:0.0}%" : string.Empty;
        var itemText = string.IsNullOrWhiteSpace(progress.CurrentItem)
            ? string.Empty
            : $" - {Path.GetFileName(progress.CurrentItem)}";

        return progress.Stage switch
        {
            WimOperationStage.Extracting => $"{StringRes.Get("WimStage_Extracting")}{percentText}{itemText}",
            WimOperationStage.Writing    => $"{StringRes.Get("WimStage_Writing")}{percentText}{itemText}",
            WimOperationStage.Verifying  => $"{StringRes.Get("WimStage_Verifying")}{percentText}{itemText}",
            WimOperationStage.Metadata   => $"{StringRes.Get("WimStage_Metadata")}{itemText}",
            WimOperationStage.Completed  => $"{StringRes.Get("WimStage_Completed")}{itemText}",
            _                            => $"{StringRes.Get("WimStage_Default")}{percentText}{itemText}"
        };
    }

    private void ApplyOperationResult(TaskOperationResult result)
    {
        OperationMessage = result.Succeeded
            ? string.Empty
            : result.Message ?? StringRes.Get("DownloadTask_OperationFailed");
    }

    public void Dispose() => _orchestrator.TaskChanged -= OnTaskChanged;
}
