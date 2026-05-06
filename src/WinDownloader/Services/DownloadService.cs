using System.Diagnostics;
using Downloader;
using WinDownloader.Interfaces;
using IDownloadService = WinDownloader.Interfaces.IDownloadService;

namespace WinDownloader.Services;

/// <summary>
/// Implements <see cref="IDownloadService"/> using the <c>Downloader</c> NuGet package.
/// <para>
/// Resume behaviour: <c>EnableAutoResumeDownload = true</c> causes the library to append
/// chunk metadata to the <c>.download</c> temporary file. On the next call with the same
/// destination path, the library detects the partial file and resumes automatically — no
/// manual <c>DownloadPackage</c> serialisation is required.
/// </para>
/// </summary>
public sealed class DownloadService : IDownloadService
{
    private readonly IAppSettings _settings;

    public DownloadService(IAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    private DownloadConfiguration BuildConfiguration() => new()
    {
        // Chunk and concurrency counts come from user settings (adjustable at runtime).
        ChunkCount = _settings.DownloadChunkCount,
        ParallelDownload = true,
        ParallelCount = _settings.DownloadParallelCount,

        // Automatic resume: metadata is embedded in the .download temp file.
        // On the next DownloadFileTaskAsync call for the same path the library
        // detects the partial file and continues from where it stopped.
        EnableAutoResumeDownload = true,

        // Memory management — release buffer every 50 MB to avoid OOM on large ESD files.
        MaximumMemoryBufferBytes = 1024L * 1024 * 50,

        // Retry up to 5 times per chunk on transient errors.
        MaxTryAgainOnFailure = 5,

        // Per-stream-block read timeout (ms).
        BlockTimeout = 3000,

        RequestConfiguration =
        {
            KeepAlive = true,
            UserAgent = "WindowsImageDownloader/1.0",
        },
    };

    /// <inheritdoc/>
    public async Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(destinationPath);
        ArgumentNullException.ThrowIfNull(progress);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var config = BuildConfiguration();
        using var downloader = new Downloader.DownloadService(config);

        // Track speed with a rolling window over the last second.
        var sw = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        var lastSpeedSample = sw.Elapsed;
        var lastProgressReport = TimeSpan.Zero;
        var progressReportInterval = TimeSpan.FromMilliseconds(500);
        var progressGate = new object();

        downloader.DownloadProgressChanged += (_, e) =>
        {
            DownloadProgress progressSnapshot;

            lock (progressGate)
            {
                var now = sw.Elapsed;
                var elapsed = now - lastSpeedSample;

                long speed;
                if (elapsed.TotalSeconds >= 0.5)
                {
                    var bytesDelta = e.ReceivedBytesSize - lastReportedBytes;
                    speed = elapsed.TotalSeconds > 0
                        ? (long)(bytesDelta / elapsed.TotalSeconds)
                        : 0;
                    lastReportedBytes = e.ReceivedBytesSize;
                    lastSpeedSample = now;
                }
                else
                {
                    // Use the library's own speed metric between our samples.
                    speed = (long)e.BytesPerSecondSpeed;
                }

                var isComplete = e.TotalBytesToReceive > 0 && e.ReceivedBytesSize >= e.TotalBytesToReceive;
                if (!isComplete && now - lastProgressReport < progressReportInterval)
                    return;

                lastProgressReport = now;
                progressSnapshot = new DownloadProgress(
                    DownloadedBytes: e.ReceivedBytesSize,
                    TotalBytes: e.TotalBytesToReceive,
                    SpeedBytesPerSecond: speed,
                    Elapsed: now);
            }

            progress.Report(progressSnapshot);
        };

        // Wire up cancellation: the library exposes CancelAsync but not a CT directly.
        await using var reg = cancellationToken.Register(static state =>
        {
            var service = (Downloader.DownloadService)state!;
            _ = Task.Run(() =>
            {
                try { service.CancelAsync(); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }, downloader);

        TaskCompletionSource<Exception?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        downloader.DownloadFileCompleted += (_, e) =>
        {
            if (e.Cancelled)
                tcs.TrySetCanceled(cancellationToken);
            else
                tcs.TrySetResult(e.Error);
        };

        await downloader.DownloadFileTaskAsync(url, destinationPath).ConfigureAwait(false);

        // DownloadFileTaskAsync already awaits completion, but errors surface through
        // the event; re-throw them here so the caller gets a proper exception.
        var error = await tcs.Task.ConfigureAwait(false);
        if (error is not null)
            throw new InvalidOperationException($"Download failed: {error.Message}", error);
    }
}
