using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

/// <summary>
/// Centralises ESD download directory and file path resolution.
/// </summary>
public sealed class DownloadTaskPathService : IDownloadTaskPathService
{
    private readonly IAppSettings _settings;

    public DownloadTaskPathService(IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public string ResolveDirectory(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var root = _settings.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("尚未设置下载目录。");
        }

        return Path.Combine(root, "WindowsImage", task.LanguageCode, task.Architecture);
    }

    public string ResolveEsdPath(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var fileBaseName = Path.GetFileNameWithoutExtension(task.FileName);
        return Path.Combine(ResolveDirectory(task), fileBaseName + ".esd");
    }

    public string ResolveIsoPath(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        var fileBaseName = Path.GetFileNameWithoutExtension(task.FileName);
        return Path.Combine(ResolveDirectory(task), fileBaseName + ".iso");
    }

    public string ResolveIsoStagingDirectory(DownloadTask task)
    {
        ArgumentNullException.ThrowIfNull(task);

        return Path.Combine(ResolveDirectory(task), ".staging");
    }

    public string ResolveTemporaryDownloadPath(DownloadTask task) =>
        ResolveEsdPath(task) + ".download";
}
