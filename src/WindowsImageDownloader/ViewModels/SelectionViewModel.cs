using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using WindowsImageDownloader.Models;
using WindowsImageDownloader.Services;

namespace WindowsImageDownloader.ViewModels;

public sealed partial class SelectionViewModel : ObservableObject
{
    private readonly IUpdateCatalogService catalogService;
    private readonly List<RawFile> allFiles = new();
    private bool isUpdatingFilters;
    private bool hasLoadAttempted;
    private bool isLoading;
    private string loadingMessage = "正在读取 Windows 产品目录...";
    private bool hasError;
    private string errorMessage = string.Empty;
    private CatalogOption? selectedLanguage;
    private CatalogOption? selectedArchitecture;
    private CatalogOption? selectedEditionGroup;
    private CatalogOption? selectedEdition;

    public SelectionViewModel(IUpdateCatalogService catalogService)
    {
        this.catalogService = catalogService;
    }

    public ObservableCollection<CatalogOption> Languages { get; } = new();

    public ObservableCollection<CatalogOption> Architectures { get; } = new();

    public ObservableCollection<CatalogOption> EditionGroups { get; } = new();

    public ObservableCollection<CatalogOption> Editions { get; } = new();

    public ObservableCollection<RawFile> FilteredFiles { get; } = new();

    public Visibility LoadingVisibility => !hasLoadAttempted || IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => hasLoadAttempted && !IsLoading && !HasError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => hasLoadAttempted && !IsLoading && !HasError && allFiles.Count > 0 && FilteredFiles.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ResultSummary => allFiles.Count == 0
        ? "暂无产品目录数据"
        : $"目录记录 {allFiles.Count:N0} 条，当前显示 {FilteredFiles.Count:N0} 条";

    public bool IsLoading
    {
        get => isLoading;
        private set
        {
            if (SetProperty(ref isLoading, value))
            {
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    public string LoadingMessage
    {
        get => loadingMessage;
        private set => SetProperty(ref loadingMessage, value);
    }

    public bool HasError
    {
        get => hasError;
        private set
        {
            if (SetProperty(ref hasError, value))
            {
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(ErrorVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public CatalogOption? SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (SetProperty(ref selectedLanguage, value) && !isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.Language);
            }
        }
    }

    public CatalogOption? SelectedArchitecture
    {
        get => selectedArchitecture;
        set
        {
            if (SetProperty(ref selectedArchitecture, value) && !isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.Architecture);
            }
        }
    }

    public CatalogOption? SelectedEditionGroup
    {
        get => selectedEditionGroup;
        set
        {
            if (SetProperty(ref selectedEditionGroup, value) && !isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.EditionGroup);
            }
        }
    }

    public CatalogOption? SelectedEdition
    {
        get => selectedEdition;
        set
        {
            if (SetProperty(ref selectedEdition, value) && !isUpdatingFilters)
            {
                RefreshFilteredFiles();
            }
        }
    }

    public Task EnsureCatalogLoadedAsync()
    {
        return allFiles.Count > 0 || IsLoading
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
            var files = await catalogService.GetCatalogAsync(forceRefresh);
            allFiles.Clear();
            allFiles.AddRange(files);
            InitializeFilters();
        }
        catch (Exception ex)
        {
            allFiles.Clear();
            ClearFilters();
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            hasLoadAttempted = true;
            IsLoading = false;
            NotifyResultStateChanged();
        }
    }

    private void InitializeFilters()
    {
        isUpdatingFilters = true;
        try
        {
            ReplaceOptions(Languages, BuildLanguageOptions());
            SelectedLanguage = Languages.FirstOrDefault();

            ReplaceOptions(Architectures, BuildArchitectureOptions());
            SelectedArchitecture = Architectures.FirstOrDefault();

            ReplaceOptions(EditionGroups, BuildEditionGroupOptions());
            SelectedEditionGroup = EditionGroups.FirstOrDefault();

            ReplaceOptions(Editions, BuildEditionOptions());
            SelectedEdition = Editions.FirstOrDefault();
        }
        finally
        {
            isUpdatingFilters = false;
        }

        RefreshFilteredFiles();
    }

    private void RefreshFilterOptions(FilterChange changedFilter)
    {
        isUpdatingFilters = true;
        try
        {
            if (changedFilter <= FilterChange.Language)
            {
                SelectedArchitecture = ReplaceOptionsPreservingSelection(
                    Architectures,
                    BuildArchitectureOptions(),
                    SelectedArchitecture);
            }

            if (changedFilter <= FilterChange.Architecture)
            {
                SelectedEditionGroup = ReplaceOptionsPreservingSelection(
                    EditionGroups,
                    BuildEditionGroupOptions(),
                    SelectedEditionGroup);
            }

            if (changedFilter <= FilterChange.EditionGroup)
            {
                SelectedEdition = ReplaceOptionsPreservingSelection(
                    Editions,
                    BuildEditionOptions(),
                    SelectedEdition);
            }
        }
        finally
        {
            isUpdatingFilters = false;
        }

        RefreshFilteredFiles();
    }

    private IEnumerable<CatalogOption> BuildLanguageOptions()
    {
        yield return CatalogOption.All("全部语言");

        foreach (var option in allFiles
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

        foreach (var architecture in allFiles
            .Where(MatchesSelectedLanguage)
            .Select(file => file.Architecture)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CatalogOption(architecture, architecture);
        }
    }

    private IEnumerable<CatalogOption> BuildEditionGroupOptions()
    {
        yield return CatalogOption.All("全部分组");

        foreach (var group in allFiles
            .Where(MatchesSelectedLanguage)
            .Where(MatchesSelectedArchitecture)
            .Select(file => file.EditionLoc)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CatalogOption(group, DescribeGroup(group));
        }
    }

    private IEnumerable<CatalogOption> BuildEditionOptions()
    {
        yield return CatalogOption.All("全部版本");

        foreach (var edition in allFiles
            .Where(MatchesSelectedLanguage)
            .Where(MatchesSelectedArchitecture)
            .Where(MatchesSelectedEditionGroup)
            .Select(file => file.Edition)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return new CatalogOption(edition, edition);
        }
    }

    private void RefreshFilteredFiles()
    {
        FilteredFiles.Clear();

        foreach (var file in allFiles
            .Where(MatchesSelectedLanguage)
            .Where(MatchesSelectedArchitecture)
            .Where(MatchesSelectedEditionGroup)
            .Where(MatchesSelectedEdition))
        {
            FilteredFiles.Add(file);
        }

        NotifyResultStateChanged();
    }

    private void ClearFilters()
    {
        isUpdatingFilters = true;
        try
        {
            Languages.Clear();
            Architectures.Clear();
            EditionGroups.Clear();
            Editions.Clear();
            FilteredFiles.Clear();
            SelectedLanguage = null;
            SelectedArchitecture = null;
            SelectedEditionGroup = null;
            SelectedEdition = null;
        }
        finally
        {
            isUpdatingFilters = false;
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

    private bool MatchesSelectedEditionGroup(RawFile file)
    {
        return IsAll(SelectedEditionGroup) ||
            string.Equals(file.EditionLoc, SelectedEditionGroup?.Value, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesSelectedEdition(RawFile file)
    {
        return IsAll(SelectedEdition) ||
            string.Equals(file.Edition, SelectedEdition?.Value, StringComparison.OrdinalIgnoreCase);
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

    private enum FilterChange
    {
        Language,
        Architecture,
        EditionGroup,
        Edition
    }
}
