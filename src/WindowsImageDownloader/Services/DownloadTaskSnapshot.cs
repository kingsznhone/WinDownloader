using WindowsImageDownloader.Models;
using WindowsImageDownloader.Iso;

namespace WindowsImageDownloader.Services;

public sealed record DownloadTaskSnapshot(
    string Sha256,
    TaskState State,
    double Progress,
    long SpeedBytesPerSecond,
    string StatusText,
    string? ErrorMessage,
    EsdToIsoTaskSnapshot? IsoConversionSnapshot = null)
{
    public static DownloadTaskSnapshot FromTask(
        DownloadTask task,
        EsdToIsoTaskSnapshot? isoConversionSnapshot = null) => new(
        task.Sha256,
        task.State,
        task.Progress,
        task.SpeedBytesPerSecond,
        task.StatusText,
        task.ErrorMessage,
        isoConversionSnapshot);
}
