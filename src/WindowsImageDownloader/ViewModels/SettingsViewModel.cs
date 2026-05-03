using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsImageDownloader.Services;

namespace WindowsImageDownloader.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettings _settings;

    public SettingsViewModel(IAppSettings settings)
    {
        _settings = settings;

        DownloadDirectory      = settings.DownloadDirectory ?? string.Empty;
        DownloadChunkCount     = settings.DownloadChunkCount;
        MaxConcurrentDownloads = settings.MaxConcurrentDownloads;
        SelectedLanguageIndex  = LanguageTagToIndex(settings.AppLanguage);
    }

    // ── Download directory ───────────────────────────────────────────────────

    [ObservableProperty]
    public partial string DownloadDirectory { get; set; }

    partial void OnDownloadDirectoryChanged(string value)
        => _settings.DownloadDirectory = string.IsNullOrWhiteSpace(value) ? null : value;

    [RelayCommand]
    private async Task BrowseDownloadDirectoryAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
        picker.FileTypeFilter.Add("*");

        // Required for WinAppSDK unpackaged: associate picker with the window HWND
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            DownloadDirectory = folder.Path;
    }

    // ── Download chunk count (1–32) ──────────────────────────────────────────

    [ObservableProperty]
    public partial int DownloadChunkCount { get; set; }

    partial void OnDownloadChunkCountChanged(int value)
        => _settings.DownloadChunkCount = value;

    // ── Max concurrent downloads (1–16) ─────────────────────────────────────

    [ObservableProperty]
    public partial int MaxConcurrentDownloads { get; set; }

    partial void OnMaxConcurrentDownloadsChanged(int value)
        => _settings.MaxConcurrentDownloads = value;

    // ── Language ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    /// <summary>Language tags ordered to match ComboBox items (index 0 = Auto).</summary>
    private static readonly string?[] LanguageTags = [null, "en-US", "zh-CN", "ja-JP", "fr-FR", "es-ES", "ko-KR"];

    public string SelectedLanguageHint
        => _settings.ResolveEffectiveLanguage();

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var tag = (value >= 0 && value < LanguageTags.Length) ? LanguageTags[value] : null;
        _settings.AppLanguage = tag;
        OnPropertyChanged(nameof(SelectedLanguageHint));
    }

    private static int LanguageTagToIndex(string? tag)
    {
        for (var i = 0; i < LanguageTags.Length; i++)
            if (string.Equals(LanguageTags[i], tag, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0; // Auto
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Reset()
    {
        _settings.Reset();
        DownloadDirectory      = _settings.DownloadDirectory ?? string.Empty;
        DownloadChunkCount     = _settings.DownloadChunkCount;
        MaxConcurrentDownloads = _settings.MaxConcurrentDownloads;
        SelectedLanguageIndex  = LanguageTagToIndex(_settings.AppLanguage);
        OnPropertyChanged(nameof(SelectedLanguageHint));
    }
}
