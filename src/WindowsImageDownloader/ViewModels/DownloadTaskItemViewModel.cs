using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;
using WindowsImageDownloader.Services;

namespace WindowsImageDownloader.ViewModels;

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

    public string FileName => Task.FileName;
    public string LanguageText => Task.LanguageText;
    public string EditionGroupText => Task.EditionGroupText;
    public TagType EditionGroupTagType => Task.EditionGroupTagType;
    public string RetailText => Task.RetailText;
    public TagType RetailTagType => Task.RetailTagType;
    public string Architecture => Task.Architecture;
    public TagType ArchTagType => Task.ArchTagType;
    public string SizeText => Task.SizeText;
    public string Sha256 => Task.Sha256;
    public IReadOnlyList<string> Editions => Task.Editions;
    public double Progress => double.IsFinite(_progress) ? _progress : 0;
    public string StatusText => _statusText;
    public string ErrorMessage => _errorMessage;

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
    private async System.Threading.Tasks.Task PauseAsync()
    {
        var result = await _orchestrator.PauseAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private async System.Threading.Tasks.Task ResumeAsync()
    {
        var result = await _orchestrator.ResumeAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async System.Threading.Tasks.Task CancelAsync()
    {
        var result = await _orchestrator.CancelAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async System.Threading.Tasks.Task DeleteAsync()
    {
        var result = await _orchestrator.DeleteAsync(Task.Sha256);
        ApplyOperationResult(result);
    }

    [RelayCommand(CanExecute = nameof(CanOpenDirectory))]
    private void OpenDirectory()
    {
        var directory = _pathService.ResolveDirectory(Task);
        if (!Directory.Exists(directory))
        {
            OperationMessage = "下载目录不存在。";
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

    /// <summary>True while the task has not yet reached a terminal state.</summary>
    public bool IsActive => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;

    public bool IsDownloadCompleted => _state == TaskState.Completed;

    public bool ShowDownloadProgress => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying;
    public bool ShowActionBar => IsDownloadCompleted;
    public bool HasStatusText => !string.IsNullOrEmpty(_statusText);
    public bool HasError => IsFailed && !string.IsNullOrEmpty(_errorMessage);
    public bool HasOperationMessage => !string.IsNullOrWhiteSpace(OperationMessage);

    public bool CanPause => _state == TaskState.Downloading;
    public bool CanResume => _state == TaskState.Queued;
    public bool CanCancel => _state is TaskState.Queued or TaskState.Downloading or TaskState.Verifying or TaskState.Failed;
    public bool CanOpenDirectory => IsDownloadCompleted;
    public bool CanDelete => _state == TaskState.Completed;
    public string CancelButtonText => IsFailed ? "移除" : "取消";

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
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDownloadCompleted));
        OnPropertyChanged(nameof(ShowDownloadProgress));
        OnPropertyChanged(nameof(ShowActionBar));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanOpenDirectory));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(CancelButtonText));
        NotifyCommandStatesChanged();
    }

    private void NotifyCommandStatesChanged()
    {
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OpenDirectoryCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void ApplyOperationResult(TaskOperationResult result)
    {
        OperationMessage = result.Succeeded
            ? string.Empty
            : result.Message ?? "操作未完成。";
    }

    public void Dispose() => _orchestrator.TaskChanged -= OnTaskChanged;
}
