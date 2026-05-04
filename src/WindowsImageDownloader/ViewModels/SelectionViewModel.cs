using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.ViewModels;

public sealed partial class SelectionViewModel : ObservableObject
{
    private readonly IUpdateCatalogService _catalogService;
    private readonly ITaskOrchestratorService _orchestrator;
    private readonly List<RawFile> _allFiles = new();
    private bool _isUpdatingFilters;
    private bool _hasLoadAttempted;
    private bool _isLoading;
    private string _loadingMessage = "正在读取 Windows 产品目录...";
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private string _operationMessage = string.Empty;
    private InfoBarSeverity _operationSeverity = InfoBarSeverity.Informational;
    private CatalogOption? _selectedLanguage;
    private CatalogOption? _selectedArchitecture;

    public SelectionViewModel(IUpdateCatalogService catalogService, ITaskOrchestratorService orchestrator)
    {
        _catalogService = catalogService;
        _orchestrator = orchestrator;
        EnqueueDownloadCommand = new AsyncRelayCommand<RawFileGroup>(EnqueueDownloadAsync);
    }

    public ObservableCollection<CatalogOption> Languages { get; } = new();

    public ObservableCollection<CatalogOption> Architectures { get; } = new();

    /// <summary>Command exposed to <see cref="Views.Controls.RawFileItemControl"/> for starting an ESD download.</summary>
    public AsyncRelayCommand<RawFileGroup> EnqueueDownloadCommand { get; }

    public ObservableCollection<RawFileItemViewModel> FilteredGroups { get; } = new();
    public Visibility LoadingVisibility => !_hasLoadAttempted || IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => _hasLoadAttempted && !IsLoading && !HasError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => _hasLoadAttempted && !IsLoading && !HasError && _allFiles.Count > 0 && FilteredGroups.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ResultSummary => _allFiles.Count == 0
        ? "暂无产品目录数据"
        : $"目录记录 {_allFiles.Count:N0} 条，当前显示 {FilteredGroups.Count:N0} 个文件";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    public string LoadingMessage
    {
        get => _loadingMessage;
        private set => SetProperty(ref _loadingMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(ErrorVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string OperationMessage
    {
        get => _operationMessage;
        private set
        {
            if (SetProperty(ref _operationMessage, value))
                OnPropertyChanged(nameof(IsOperationMessageOpen));
        }
    }

    public InfoBarSeverity OperationSeverity
    {
        get => _operationSeverity;
        private set => SetProperty(ref _operationSeverity, value);
    }

    public bool IsOperationMessageOpen
    {
        get => !string.IsNullOrWhiteSpace(OperationMessage);
        set
        {
            if (!value)
                OperationMessage = string.Empty;
        }
    }

    public CatalogOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && !_isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.Language);
            }
        }
    }

    public CatalogOption? SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (SetProperty(ref _selectedArchitecture, value) && !_isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.Architecture);
            }
        }
    }

    public Task EnsureCatalogLoadedAsync()
    {
        return _allFiles.Count > 0 || IsLoading
            ? Task.CompletedTask
            : LoadCatalogAsync(forceRefresh: false);
    }

    [RelayCommand]
    private Task ReloadAsync()
    {
        return LoadCatalogAsync(forceRefresh: true);
    }

    private async Task LoadCatalogAsync(bool forceRefresh)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        LoadingMessage = forceRefresh ? "正在刷新远程产品目录..." : "正在读取 Windows 产品目录...";

        try
        {
            var files = await _catalogService.GetCatalogAsync(forceRefresh);
            _allFiles.Clear();
            _allFiles.AddRange(files);
            InitializeFilters();
        }
        catch (Exception ex)
        {
            _allFiles.Clear();
            ClearFilters();
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            _hasLoadAttempted = true;
            IsLoading = false;
            NotifyResultStateChanged();
        }
    }

    private void InitializeFilters()
    {
        _isUpdatingFilters = true;
        try
        {
            ReplaceOptions(Languages, BuildLanguageOptions());
            SelectedLanguage = Languages.FirstOrDefault();

            ReplaceOptions(Architectures, BuildArchitectureOptions());
            SelectedArchitecture = Architectures.FirstOrDefault();
        }
        finally
        {
            _isUpdatingFilters = false;
        }

        RefreshFilteredFiles();
    }

    private void RefreshFilterOptions(FilterChange changedFilter)
    {
        _isUpdatingFilters = true;
        try
        {
            if (changedFilter <= FilterChange.Language)
            {
                SelectedArchitecture = ReplaceOptionsPreservingSelection(
                    Architectures,
                    BuildArchitectureOptions(),
                    SelectedArchitecture);
            }

            }
        finally
        {
            _isUpdatingFilters = false;
        }

        RefreshFilteredFiles();
    }

    private IEnumerable<CatalogOption> BuildLanguageOptions()
    {
        yield return CatalogOption.All("全部语言");

        foreach (var option in _allFiles
            .GroupBy(file => file.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var label = string.IsNullOrWhiteSpace(first.Language)
                    ? first.LanguageCode
                    : $"{first.LanguageCode} - {first.Language}";
                return new CatalogOption(first.LanguageCode, label);
            })
            .OrderBy(option => option.Value, StringComparer.OrdinalIgnoreCase))
        {
            yield return option;
        }
    }

    private IEnumerable<CatalogOption> BuildArchitectureOptions()
    {
        yield return CatalogOption.All("全部架构");

        foreach (var architecture in _allFiles
            .Where(MatchesSelectedLanguage)
            .Select(file => file.Architecture)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CatalogOption(architecture, architecture);
        }
    }

    private void RefreshFilteredFiles()
    {
        FilteredGroups.Clear();

        foreach (var group in _allFiles
            .Where(MatchesSelectedLanguage)
            .Where(MatchesSelectedArchitecture)
            .GroupBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var representative = g.First();
                var editions = g
                    .Select(f => f.Edition)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new RawFileGroup(representative, editions);
            }))
        {
            FilteredGroups.Add(new RawFileItemViewModel(group, EnqueueDownloadCommand));
        }

        NotifyResultStateChanged();
    }

    private void ClearFilters()
    {
        _isUpdatingFilters = true;
        try
        {
            Languages.Clear();
            Architectures.Clear();
            FilteredGroups.Clear();
            SelectedLanguage = null;
            SelectedArchitecture = null;
        }
        finally
        {
            _isUpdatingFilters = false;
        }
    }

    private bool MatchesSelectedLanguage(RawFile file)
    {
        return IsAll(SelectedLanguage) ||
            string.Equals(file.LanguageCode, SelectedLanguage?.Value, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSelectedArchitecture(RawFile file)
    {
        return IsAll(SelectedArchitecture) ||
            string.Equals(file.Architecture, SelectedArchitecture?.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAll(CatalogOption? option) => option is null || option.IsAll;

    private static void ReplaceOptions(
        ObservableCollection<CatalogOption> target,
        IEnumerable<CatalogOption> options)
    {
        target.Clear();
        foreach (var option in options)
        {
            target.Add(option);
        }
    }

    private static CatalogOption? ReplaceOptionsPreservingSelection(
        ObservableCollection<CatalogOption> target,
        IEnumerable<CatalogOption> options,
        CatalogOption? currentSelection)
    {
        ReplaceOptions(target, options);
        return target.FirstOrDefault(option => option.Value == currentSelection?.Value) ?? target.FirstOrDefault();
    }

    private static string DescribeGroup(string editionLoc) => editionLoc switch
    {
        "%CLIENT%" => "消费者零售版（家庭版/专业版/教育版系列）",
        "%ENTERPRISE%" => "企业批量许可版",
        "%ENTERPRISE_N%" => "企业批量许可版 N（无媒体播放器）",
        "%BASE_CHINA%" => "中国特供版",
        _ => editionLoc
    };

    private void NotifyResultStateChanged()
    {
        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(ResultSummary));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private async Task EnqueueDownloadAsync(RawFileGroup? group)
    {
        if (group is null)
        {
            return;
        }

        OperationMessage = string.Empty;

        try
        {
            var task = DownloadTask.FromRawFile(group.File, group.Editions);
            var result = await _orchestrator.EnqueueAsync(task);

            OperationSeverity = result.Succeeded
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            OperationMessage = result.Message ?? (result.Succeeded ? "已添加到下载任务。" : "任务未添加。");
        }
        catch (OperationCanceledException)
        {
            OperationSeverity = InfoBarSeverity.Warning;
            OperationMessage = "添加下载任务已取消。";
        }
        catch (Exception ex)
        {
            OperationSeverity = InfoBarSeverity.Error;
            OperationMessage = $"无法添加下载任务：{ex.Message}";
        }
    }

    private enum FilterChange
    {
        Language,
        Architecture
    }
}
