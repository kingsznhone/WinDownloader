using Microsoft.Windows.ApplicationModel.Resources;

namespace WinDownloader.Helpers;

internal static class StringRes
{
    private static readonly ResourceLoader _loader = new();

    public static string Get(string key) => _loader.GetString(key);
}
