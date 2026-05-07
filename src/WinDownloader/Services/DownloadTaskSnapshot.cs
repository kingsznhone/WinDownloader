using WinDownloader.Models;

namespace WinDownloader.Services;

public sealed record DownloadTaskSnapshot(
    string Sha256,
    TaskState State,
    double Progress,
    long SpeedBytesPerSecond,
    string StatusText,
    string? ErrorMessage)
{
    public static DownloadTaskSnapshot FromTask(DownloadTask task) => new(
        task.Sha256,
        task.State,
        task.Progress,
        task.SpeedBytesPerSecond,
        task.StatusText,
        task.ErrorMessage);
}
