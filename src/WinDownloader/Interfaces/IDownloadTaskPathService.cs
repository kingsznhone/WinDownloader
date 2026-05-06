using WinDownloader.Models;

namespace WinDownloader.Interfaces;

/// <summary>
/// Resolves file-system paths for ESD download tasks.
/// </summary>
public interface IDownloadTaskPathService
{
    /// <summary>Resolves the directory that contains all files for <paramref name="task"/>.</summary>
    string ResolveDirectory(DownloadTask task);

    /// <summary>Resolves the final ESD file path for <paramref name="task"/>.</summary>
    string ResolveEsdPath(DownloadTask task);

    /// <summary>Resolves the final ISO file path for <paramref name="task"/>.</summary>
    string ResolveIsoPath(DownloadTask task);

    /// <summary>Resolves the ISO conversion staging directory for <paramref name="task"/>.</summary>
    string ResolveIsoStagingDirectory(DownloadTask task);

    /// <summary>Resolves the Downloader temporary file path for <paramref name="task"/>.</summary>
    string ResolveTemporaryDownloadPath(DownloadTask task);
}
