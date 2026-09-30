// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDownloader.Helpers;
using WinDownloader.Interfaces;
using WinDownloader.Models;
using WinDownloader.Services;

namespace WinDownloader.ViewModels;

public sealed partial class SelectionViewModel : ObservableObject
{
    private readonly IUpdateCatalogService _catalogService;
    private readonly IDownloadTaskOrchestratorService _orchestrator;
    private readonly List<RawFile> _allFiles = [];
    private bool _isUpdatingFilters;
    private bool _hasLoadAttempted;

    public SelectionViewModel(IUpdateCatalogService catalogService, IDownloadTaskOrchestratorService orchestrator)
    {
        _catalogService = catalogService;
        _orchestrator = orchestrator;
        EnqueueDownloadCommand = new AsyncRelayCommand<RawFileGroup>(EnqueueDownloadAsync);
    }

    public ObservableCollection<CatalogOption> Languages { get; } = [];

    public ObservableCollection<CatalogOption> Architectures { get; } = [];

    /// <summary>Command exposed to <see cref="Views.Controls.RawFileItemControl"/> for starting an ESD download.</summary>
    public AsyncRelayCommand<RawFileGroup> EnqueueDownloadCommand { get; }

    public ObservableCollection<RawFileItemViewModel> FilteredGroups { get; } = [];
    public Visibility LoadingVisibility => !_hasLoadAttempted || IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentVisibility => _hasLoadAttempted && !IsLoading && !HasError
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ErrorVisibility => HasError ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyVisibility => _hasLoadAttempted && !IsLoading && !HasError && _allFiles.Count > 0 && FilteredGroups.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ResultSummary => _allFiles.Count == 0
        ? StringRes.Get("Selection_NoData")
        : string.Format(StringRes.Get("Selection_ResultSummaryFormat"), _allFiles.Count.ToString("N0"), FilteredGroups.Count.ToString("N0"));

    public bool IsLoading
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    [ObservableProperty]
    public partial string LoadingMessage { get; private set; } = StringRes.Get("Selection_LoadingMessage");

    public bool HasError
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(ContentVisibility));
                OnPropertyChanged(nameof(ErrorVisibility));
                OnPropertyChanged(nameof(EmptyVisibility));
            }
        }
    }

    [ObservableProperty]
    public partial string ErrorMessage { get; private set; } = string.Empty;

    public string OperationMessage
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsOperationMessageOpen));
            }
        }
    } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity OperationSeverity { get; private set; } = InfoBarSeverity.Informational;

    public bool IsOperationMessageOpen
    {
        get => !string.IsNullOrWhiteSpace(OperationMessage);
        set
        {
            if (!value)
            {
                OperationMessage = string.Empty;
            }
        }
    }

    public CatalogOption? SelectedLanguage
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_isUpdatingFilters)
            {
                RefreshFilterOptions(FilterChange.Language);
            }
        }
    }

    public CatalogOption? SelectedArchitecture
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && !_isUpdatingFilters)
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
        LoadingMessage = StringRes.Get(forceRefresh ? "Selection_RefreshingMessage" : "Selection_LoadingMessage");

        try
        {
            IReadOnlyList<RawFile> files = await _catalogService.GetCatalogAsync(forceRefresh);
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
        yield return CatalogOption.All(StringRes.Get("Selection_AllLanguages"));

        foreach (CatalogOption? option in _allFiles
            .GroupBy(file => file.LanguageCode, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                RawFile first = group.First();
                string label = string.IsNullOrWhiteSpace(first.Language)
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
        yield return CatalogOption.All(StringRes.Get("Selection_AllArchitectures"));

        foreach (string? architecture in _allFiles
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

        foreach (RawFileGroup? group in _allFiles
            .Where(MatchesSelectedLanguage)
            .Where(MatchesSelectedArchitecture)
            .GroupBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                RawFile representative = g.First();
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
        foreach (CatalogOption option in options)
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
            var task = DownloadTask.FromRawFileGroup(group);
            TaskOperationResult result = await _orchestrator.EnqueueAsync(task);

            OperationSeverity = result.Succeeded
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            OperationMessage = result.Message ?? (result.Succeeded ? StringRes.Get("Selection_EnqueueSuccess") : StringRes.Get("Selection_EnqueueNotAdded"));
        }
        catch (OperationCanceledException)
        {
            OperationSeverity = InfoBarSeverity.Warning;
            OperationMessage = StringRes.Get("Selection_EnqueueCancelled");
        }
        catch (Exception ex)
        {
            OperationSeverity = InfoBarSeverity.Error;
            OperationMessage = string.Format(StringRes.Get("Selection_EnqueueErrorFormat"), ex.Message);
        }
    }

    private enum FilterChange
    {
        Language,
        Architecture
    }
}
