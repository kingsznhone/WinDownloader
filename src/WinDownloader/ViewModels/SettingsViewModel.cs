using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDownloader.Interfaces;

namespace WinDownloader.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettings _settings;

    public SettingsViewModel(IAppSettings settings)
    {
        _settings = settings;

        DownloadDirectory      = settings.DownloadDirectory!;
        DownloadChunkCount     = settings.DownloadChunkCount;
        DownloadParallelCount  = settings.DownloadParallelCount;
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

    // ── Download chunk count (1–128) ──────────────────────────────────────────

    [ObservableProperty]
    public partial int DownloadChunkCount { get; set; }

    partial void OnDownloadChunkCountChanged(int value)
        => _settings.DownloadChunkCount = value;

    // ── Download parallel count (1–16) ──────────────────────────────────────

    [ObservableProperty]
    public partial int DownloadParallelCount { get; set; }

    partial void OnDownloadParallelCountChanged(int value)
        => _settings.DownloadParallelCount = value;

    // ── Max concurrent downloads (1–16) ─────────────────────────────────────

    [ObservableProperty]
    public partial int MaxConcurrentDownloads { get; set; }

    partial void OnMaxConcurrentDownloadsChanged(int value)
        => _settings.MaxConcurrentDownloads = value;

    // ── Language ─────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int SelectedLanguageIndex { get; set; }

    /// <summary>Language tags ordered to match ComboBox items (index 0 = Auto).</summary>
    private static readonly string?[] _languageTags = [null, "en-US", "zh-CN"];

    public string SelectedLanguageHint
        => _settings.ResolveEffectiveLanguage();

    [ObservableProperty]
    public partial bool IsRestartRequired { get; set; }

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        var tag = (value >= 0 && value < _languageTags.Length) ? _languageTags[value] : null;
        _settings.AppLanguage = tag;
        OnPropertyChanged(nameof(SelectedLanguageHint));
        IsRestartRequired = true;
    }

    private static int LanguageTagToIndex(string? tag)
    {
        for (var i = 0; i < _languageTags.Length; i++)
            if (string.Equals(_languageTags[i], tag, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0; // Auto
    }

    // ── Restart ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private static void RestartApp()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });

            App.MainWindow.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[SettingsViewModel] Failed to restart app: {ex.Message}");
        }
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Reset()
    {
        _settings.Reset();
        DownloadDirectory      = _settings.DownloadDirectory!;
        DownloadChunkCount     = _settings.DownloadChunkCount;
        DownloadParallelCount  = _settings.DownloadParallelCount;
        MaxConcurrentDownloads = _settings.MaxConcurrentDownloads;
        SelectedLanguageIndex  = LanguageTagToIndex(_settings.AppLanguage);
        OnPropertyChanged(nameof(SelectedLanguageHint));
    }
}
