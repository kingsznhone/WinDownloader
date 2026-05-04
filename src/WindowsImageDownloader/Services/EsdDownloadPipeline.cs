using System.Security.Cryptography;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

/// <summary>
/// Handles the file-level ESD download and checksum verification work for a single task.
/// </summary>
public sealed class EsdDownloadPipeline : IEsdDownloadPipeline
{
    private readonly IDownloadService _downloadService;
    private readonly IDownloadTaskPathService _pathService;

    public EsdDownloadPipeline(IDownloadService downloadService, IDownloadTaskPathService pathService)
    {
        ArgumentNullException.ThrowIfNull(downloadService);
        ArgumentNullException.ThrowIfNull(pathService);

        _downloadService = downloadService;
        _pathService = pathService;
    }

    public async Task DownloadAsync(
        DownloadTask task,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(progress);

        var esdPath = _pathService.ResolveEsdPath(task);
        Directory.CreateDirectory(Path.GetDirectoryName(esdPath)!);

        await _downloadService.DownloadAsync(
            task.DownloadUrl,
            esdPath,
            progress,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task VerifyAsync(DownloadTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var esdPath = _pathService.ResolveEsdPath(task);
        var actualHash = await ComputeSha256Async(esdPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, task.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SHA-256 不匹配。期望: {task.Sha256}  实际: {actualHash}");
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
