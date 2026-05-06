using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using WindowsImageDownloader.Interfaces;

namespace WindowsImageDownloader.Services;

/// <summary>
/// Persists application settings in a JSON file (unpackaged) with
/// <see cref="INotifyPropertyChanged"/> support for TwoWay bindings.
/// </summary>
public sealed class AppSettingsService : IAppSettings
{
    private readonly JsonSettingsStore _store;

    public AppSettingsService()
    {
        _store = JsonSettingsStore.Create();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>
    /// Number of chunks to split a download into for multi-threaded downloading.
    /// Default: 4. Clamped to 1–32.
    /// </summary>
    public int DownloadChunkCount
    {
        get => Math.Clamp(Get(Keys.DownloadChunkCount, Defaults.DownloadChunkCount), 1, 256);
        set => Set(Keys.DownloadChunkCount, Math.Clamp(value, 1, 256));
    }

    /// <summary>
    /// Number of parallel HTTP streams per download (≤ ChunkCount).
    /// Default: 4. Clamped to 1–32.
    /// </summary>
    public int DownloadParallelCount
    {
        get => Math.Clamp(Get(Keys.DownloadParallelCount, Defaults.DownloadParallelCount), 1, 16);
        set => Set(Keys.DownloadParallelCount, Math.Clamp(value, 1, 16));
    }

    /// <summary>
    /// Maximum number of download tasks running concurrently.
    /// Default: 1. Clamped to 1–16.
    /// </summary>
    public int MaxConcurrentDownloads
    {
        get => Math.Clamp(Get(Keys.MaxConcurrentDownloads, Defaults.MaxConcurrentDownloads), 1, 16);
        set => Set(Keys.MaxConcurrentDownloads, Math.Clamp(value, 1, 16));
    }

    /// <summary>
    /// Directory where downloaded ESD files are saved.
    /// </summary>
    public string? DownloadDirectory
    {
        get => Get<string?>(Keys.DownloadDirectory, null) ?? Defaults.DownloadDirectory;
        set
        {
            // Store null when the value matches the default (keeps settings file clean)
            if (value is null || value == Defaults.DownloadDirectory)
                Remove(Keys.DownloadDirectory);
            else
                Set(Keys.DownloadDirectory, value);
        }
    }

    /// <summary>
    /// UI culture override (e.g. "zh-CN", "en-US"). Null means follow system.
    /// </summary>
    public string? AppLanguage
    {
        get => Get<string?>(Keys.AppLanguage, null);
        set
        {
            var normalized = NormalizeSupportedLanguage(value);
            if (normalized is null)
                Remove(Keys.AppLanguage);
            else
                Set(Keys.AppLanguage, normalized);
        }
    }

    // ── Language resolution ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ResolveEffectiveLanguage()
    {
        var saved = AppLanguage;
        var normalizedSaved = NormalizeSupportedLanguage(saved);
        if (normalizedSaved is not null)
            return normalizedSaved;

        var systemLang = CultureInfo.CurrentUICulture.Name;

        return systemLang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
    }

    private static string? NormalizeSupportedLanguage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, "en-US", StringComparison.OrdinalIgnoreCase))
            return "en-US";

        if (string.Equals(value, "zh-CN", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        return null;
    }

    // ── Defaults & reset ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void EnsureDefaults()
    {
        void SetIfMissing(string key, object value)
        {
            if (!_store.TryGetValue(key, out _))
                _store.SetValue(key, value);
        }

        SetIfMissing(Keys.DownloadChunkCount,     Defaults.DownloadChunkCount);
        SetIfMissing(Keys.DownloadParallelCount,  Defaults.DownloadParallelCount);
        SetIfMissing(Keys.MaxConcurrentDownloads, Defaults.MaxConcurrentDownloads);
        // DownloadDirectory and AppLanguage intentionally omitted (null default)
    }

    /// <inheritdoc/>
    public void Reset()
    {
        DownloadChunkCount = Defaults.DownloadChunkCount;
        DownloadParallelCount = Defaults.DownloadParallelCount;
        MaxConcurrentDownloads = Defaults.MaxConcurrentDownloads;
        DownloadDirectory = null;
        AppLanguage = null;
    }

    // ── Store helpers ────────────────────────────────────────────────────────

    private T Get<T>(string key, T defaultValue)
    {
        if (_store.TryGetValue(key, out var raw) && TryCoerce(raw, out T typed))
            return typed;
        return defaultValue;
    }

    private void Set(string key, object value)
    {
        if (_store.TryGetValue(key, out var existing) && ValuesEqual(existing, value))
            return;
        _store.SetValue(key, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    private void Remove(string key)
    {
        if (_store.Remove(key))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));
    }

    private static bool ValuesEqual(object? existing, object value)
    {
        if (existing?.Equals(value) == true)
            return true;

        return TryConvert(existing, value.GetType(), out var converted) && converted?.Equals(value) == true;
    }

    private static bool TryCoerce<T>(object? raw, out T value)
    {
        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        if (TryConvert(raw, typeof(T), out var converted))
        {
            if (converted is T convertedTyped)
            {
                value = convertedTyped;
                return true;
            }

            if (converted is null && default(T) is null)
            {
                value = default!;
                return true;
            }
        }

        value = default!;
        return false;
    }

    private static bool TryConvert(object? raw, Type targetType, out object? value)
    {
        value = null;
        var conversionType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (raw is null)
            return !conversionType.IsValueType;

        try
        {
            if (conversionType == typeof(string))
            {
                value = raw.ToString();
                return true;
            }

            if (raw is IConvertible)
            {
                value = Convert.ChangeType(raw, conversionType, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }

        return false;
    }

    // ── JSON-backed store ────────────────────────────────────────────────────

    private sealed class JsonSettingsStore
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private readonly string _filePath;
        private readonly Dictionary<string, object?> _values;

        private JsonSettingsStore(string filePath, Dictionary<string, object?> values)
        {
            _filePath = filePath;
            _values = values;
        }

        public static JsonSettingsStore Create()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsImageDownloader");
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, "settings.json");
            return new JsonSettingsStore(filePath, Load(filePath));
        }

        public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

        public void SetValue(string key, object value)
        {
            _values[key] = value;
            Save();
        }

        public bool Remove(string key)
        {
            if (!_values.Remove(key)) return false;
            Save();
            return true;
        }

        private static Dictionary<string, object?> Load(string filePath)
        {
            if (!File.Exists(filePath)) return [];

            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(filePath));
                if (raw is null) return [];

                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var item in raw)
                    values[item.Key] = ReadJsonValue(item.Value);
                return values;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[AppSettingsService] Failed to load settings: {ex.Message}");
                return [];
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_values, _jsonOptions));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[AppSettingsService] Failed to save settings: {ex.Message}");
            }
        }

        private static object? ReadJsonValue(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
            JsonValueKind.Number when value.TryGetUInt32(out var uintValue) => uintValue,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Null => null,
            _ => value.GetRawText(),
        };
    }

    // ── Key constants ────────────────────────────────────────────────────────

    private static class Keys
    {
        public const string DownloadChunkCount = nameof(DownloadChunkCount);
        public const string DownloadParallelCount = nameof(DownloadParallelCount);
        public const string MaxConcurrentDownloads = nameof(MaxConcurrentDownloads);
        public const string DownloadDirectory = nameof(DownloadDirectory);
        public const string AppLanguage = nameof(AppLanguage);
    }

    // ── Factory defaults ─────────────────────────────────────────────────────

    internal static class Defaults
    {
        public const int DownloadChunkCount = 32;
        public const int DownloadParallelCount = 4;
        public const int MaxConcurrentDownloads = 1;
        public const string? AppLanguage = null; // null = follow system

        /// <summary>
        /// Returns the user's Downloads folder as reported by Windows
        /// (respects folder relocations set in Explorer).
        /// </summary>
        public static string DownloadDirectory
            => Windows.Storage.UserDataPaths.GetDefault().Downloads;
    }
}
