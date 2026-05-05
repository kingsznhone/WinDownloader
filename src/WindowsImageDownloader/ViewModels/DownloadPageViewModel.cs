using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;
using WindowsImageDownloader.Services;

namespace WindowsImageDownloader.ViewModels;

public sealed partial class DownloadPageViewModel : ObservableObject, IDisposable
{
    private readonly ITaskOrchestratorService _orchestrator;
    private readonly IDownloadTaskPathService _pathService;
    private readonly DispatcherQueue _dispatcherQueue;

    public DownloadPageViewModel(
        ITaskOrchestratorService orchestrator,
        IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(pathService);

        _orchestrator = orchestrator;
        _pathService = pathService;
        // Captured on UI thread; orchestrator events arrive on thread-pool threads.
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Populate from tasks already loaded at startup.
        foreach (var task in orchestrator.Tasks)
            Items.Add(new DownloadTaskItemViewModel(task, orchestrator, pathService));

        orchestrator.TaskAdded   += OnTaskAdded;
        orchestrator.TaskRemoved += OnTaskRemoved;
        orchestrator.TaskChanged += OnTaskChanged;
        orchestrator.ActiveTaskCountChanged += OnActiveTaskCountChanged;

        UpdatePendingCount();
    }

    public ObservableCollection<DownloadTaskItemViewModel> Items { get; } = [];

    public bool HasItems => Items.Count > 0;
    public bool ShowEmptyState => Items.Count == 0;

    // ── InfoBadge ─────────────────────────────────────────────────────────────

    private int _pendingTaskCount;

    /// <summary>Count of active download or ISO conversion workers.</summary>
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
            Items.Insert(0, new DownloadTaskItemViewModel(task, _orchestrator, _pathService));
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
        PendingTaskCount = _orchestrator.ActiveTaskCount;
    }

    private void NotifyItemsStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmptyState));
        UpdatePendingCount();
    }

    public void Dispose()
    {
        _orchestrator.TaskAdded   -= OnTaskAdded;
        _orchestrator.TaskRemoved -= OnTaskRemoved;
        _orchestrator.TaskChanged -= OnTaskChanged;
        _orchestrator.ActiveTaskCountChanged -= OnActiveTaskCountChanged;
        foreach (var item in Items)
            item.Dispose();
    }
}
