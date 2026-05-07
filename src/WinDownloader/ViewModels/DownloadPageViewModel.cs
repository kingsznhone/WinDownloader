using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinDownloader.Interfaces;
using WinDownloader.Models;
using WinDownloader.Services;

namespace WinDownloader.ViewModels;

public sealed partial class DownloadPageViewModel : ObservableObject, IDisposable
{
    private readonly IDownloadTaskOrchestratorService _downloadOrchestrator;
    private readonly IEsdToIsoOrchestratorService _isoOrchestrator;
    private readonly IDownloadTaskPathService _pathService;
    private readonly DispatcherQueue _dispatcherQueue;

    public DownloadPageViewModel(
        IDownloadTaskOrchestratorService downloadOrchestrator,
        IEsdToIsoOrchestratorService isoOrchestrator,
        IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(downloadOrchestrator);
        ArgumentNullException.ThrowIfNull(isoOrchestrator);
        ArgumentNullException.ThrowIfNull(pathService);

        _downloadOrchestrator = downloadOrchestrator;
        _isoOrchestrator = isoOrchestrator;
        _pathService = pathService;
        // Captured on UI thread; service events arrive on thread-pool threads.
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Populate from tasks already loaded at startup.
        foreach (var task in downloadOrchestrator.Tasks)
            Items.Add(new DownloadTaskItemViewModel(task, downloadOrchestrator, isoOrchestrator, pathService));

        downloadOrchestrator.TaskAdded   += OnTaskAdded;
        downloadOrchestrator.TaskRemoved += OnTaskRemoved;
        downloadOrchestrator.TaskChanged += OnTaskChanged;
        downloadOrchestrator.ActiveTaskCountChanged += OnActiveTaskCountChanged;
        isoOrchestrator.ActiveTaskCountChanged += OnActiveTaskCountChanged;

        UpdatePendingCount();
    }

    public ObservableCollection<DownloadTaskItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;
    public bool ShowEmptyState => Items.Count == 0;

    // ── InfoBadge ─────────────────────────────────────────────────────────────

    private int _pendingTaskCount;

    /// <summary>Count of active download and ISO conversion workers.</summary>
    public int PendingTaskCount
    {
        get => _pendingTaskCount;
        private set
        {
            if (SetProperty(ref _pendingTaskCount, value))
                OnPropertyChanged(nameof(PendingTaskBadgeVisibility));
        }
    }

    /// <summary>Visibility of the InfoBadge; hidden when no task is active.</summary>
    public Visibility PendingTaskBadgeVisibility =>
        PendingTaskCount > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnTaskAdded(object? sender, DownloadTask task) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            Items.Insert(0, new DownloadTaskItemViewModel(task, _downloadOrchestrator, _isoOrchestrator, _pathService));
            NotifyItemsStateChanged();
        });

    private void OnTaskRemoved(object? sender, DownloadTask task) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            var vm = Items.FirstOrDefault(x => x.Task.Sha256 == task.Sha256);
            if (vm is not null)
            {
                Items.Remove(vm);
                vm.Dispose();
            }
            NotifyItemsStateChanged();
        });

    private void OnTaskChanged(object? sender, DownloadTaskSnapshot e) =>
        _dispatcherQueue.TryEnqueue(UpdatePendingCount);

    private void OnActiveTaskCountChanged(object? sender, EventArgs e) =>
        _dispatcherQueue.TryEnqueue(UpdatePendingCount);

    private void UpdatePendingCount()
    {
        PendingTaskCount = _downloadOrchestrator.ActiveTaskCount + _isoOrchestrator.ActiveTaskCount;
    }

    private void NotifyItemsStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmptyState));
        UpdatePendingCount();
    }

    public void Dispose()
    {
        _downloadOrchestrator.TaskAdded   -= OnTaskAdded;
        _downloadOrchestrator.TaskRemoved -= OnTaskRemoved;
        _downloadOrchestrator.TaskChanged -= OnTaskChanged;
        _downloadOrchestrator.ActiveTaskCountChanged -= OnActiveTaskCountChanged;
        _isoOrchestrator.ActiveTaskCountChanged -= OnActiveTaskCountChanged;
        foreach (var item in Items)
            item.Dispose();
    }
}
