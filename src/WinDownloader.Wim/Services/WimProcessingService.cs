using ManagedWimLib;
using ManagedWim = ManagedWimLib.Wim;

namespace WinDownloader.Wim;

/// <summary>
/// Singleton Only - 该服务内部维护了一个全局的 WIM 库实例，并使用信号量来确保同一时间只有一个操作在进行，以避免线程安全问题。
/// </summary>
public sealed class WimProcessingService : IWimProcessingService, IDisposable
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private volatile int _isDisposed;
    private readonly string? _initErrorMessage;

    public WimProcessingService()
    {
        // NuGet places the native library at different locations depending on build type:
        // - Debug / non-self-contained: runtimes/win-x64/native/libwim-15.dll
        // - Self-contained / published (runtimes flattened): libwim-15.dll at the output root
        var nativeLibraryPath =
            TryFindNativeLibrary("runtimes", "win-x64", "native", "libwim-15.dll")
            ?? TryFindNativeLibrary("libwim-15.dll");

        if (nativeLibraryPath is not null)
        {
            ManagedWim.GlobalInit(nativeLibraryPath, InitFlags.None);
        }
        else
        {
            // Native library not found — defer the error to operation time so the host can start normally.
            _initErrorMessage = $"Can't find libwim-15.dll";
        }
    }

    private static string? TryFindNativeLibrary(params string[] pathSegments)
    {
        var path = Path.Combine([AppContext.BaseDirectory, .. pathSegments]);
        return File.Exists(path) ? path : null;
    }

    private void ThrowIfUnavailable()
    {
        if (_initErrorMessage is not null)
            throw new PlatformNotSupportedException(_initErrorMessage);
    }

    public async Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath, nameof(imagePath));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"File not found: {imagePath}", imagePath);
        ThrowIfDisposed();
        ThrowIfUnavailable();

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var wim = ManagedWim.OpenWim(imagePath, OpenFlags.None);
                var wimInfo = wim.GetWimInfo();
                var images = new List<WimImageInfo>((int)wimInfo.ImageCount);

                for (var index = 1; index <= wimInfo.ImageCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    images.Add(GetImageInfo(wim, wimInfo.BootIndex, index));
                }

                return images;
            }, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ExtractImageAsync(
        WimExtractRequest request,
        Action<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImagePath, nameof(request.SourceImagePath));
        if (!File.Exists(request.SourceImagePath))
            throw new FileNotFoundException($"File not found: {request.SourceImagePath}", request.SourceImagePath);
        if (request.ImageIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(request.ImageIndex), request.ImageIndex, "WIM image index starts from 1.");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationDirectory, nameof(request.DestinationDirectory));

        ThrowIfDisposed();
        ThrowIfUnavailable();
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(request.DestinationDirectory);

                using var wim = ManagedWim.OpenWim(request.SourceImagePath, OpenFlags.None);
                var callback = CreateProgressCallback(progress, cancellationToken);
                if (callback is not null)
                {
                    wim.RegisterCallback(callback);
                }
                wim.ExtractImage(request.ImageIndex, request.DestinationDirectory, ExtractFlags.None);
                progress?.Invoke(new WimOperationProgress(WimOperationStage.Completed, 100, null, null, request.DestinationDirectory));
            }, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ExportImagesAsync(
        WimExportRequest request,
        Action<WimOperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceImagePath, nameof(request.SourceImagePath));
        if (!File.Exists(request.SourceImagePath))
            throw new FileNotFoundException($"File not found: {request.SourceImagePath}", request.SourceImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationImagePath, nameof(request.DestinationImagePath));

        if (request.Images.Count == 0)
        {
            throw new ArgumentException("At least one image is required for export.", nameof(request.Images));
        }

        ThrowIfDisposed();
        ThrowIfUnavailable();
        await _operationLock.WaitAsync(ct);
        try
        {
            ThrowIfDisposed();
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var destinationDirectory = Path.GetDirectoryName(request.DestinationImagePath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                if (File.Exists(request.DestinationImagePath))
                {
                    File.Delete(request.DestinationImagePath);
                }

                var callback = CreateProgressCallback(progress, ct);
                using var sourceWim = ManagedWim.OpenWim(request.SourceImagePath, OpenFlags.None);
                using var destinationWim = ManagedWim.CreateNewWim(request.Compression);
                destinationWim.SetOutputCompressionType(request.Compression);
                destinationWim.SetOutputPackCompressionType(request.Compression);
                if (request.OutputChunkSize > 0)
                    destinationWim.SetOutputChunkSize(request.OutputChunkSize);
                if (request.OutputPackChunkSize > 0)
                    destinationWim.SetOutputPackChunkSize(request.OutputPackChunkSize);

                if (callback is not null)
                {
                    sourceWim.RegisterCallback(callback);
                    destinationWim.RegisterCallback(callback);
                }

                foreach (var image in request.Images)
                {
                    ct.ThrowIfCancellationRequested();
                    sourceWim.ExportImage(
                        image.ImageIndex,
                        destinationWim,
                        image.ImageName,
                        image.ImageDescription,
                        image.ExportFlags);

                    progress?.Invoke(new WimOperationProgress(
                        WimOperationStage.Metadata,
                        null,
                        null,
                        null,
                        image.ImageName));
                }

                var writeFlags = WriteFlags.None;
                if (request.CheckIntegrity) writeFlags |= WriteFlags.CheckIntegrity;
                if (request.Recompress) writeFlags |= WriteFlags.Recompress;
                if (request.Solid) writeFlags |= WriteFlags.Solid;
                destinationWim.Write(request.DestinationImagePath, ManagedWim.AllImages, writeFlags, 0);
                progress?.Invoke(new WimOperationProgress(WimOperationStage.Completed, 100, null, null, request.DestinationImagePath));
            }, ct);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            return;

        _operationLock.Dispose();
        if (_initErrorMessage is null)
            ManagedWim.TryGlobalCleanup();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed != 0, this);

    private static WimImageInfo GetImageInfo(ManagedWim wim, uint bootIndex, int index)
    {
        var displayName = wim.GetImageProperty(index, "DISPLAYNAME")
            ?? wim.GetImageProperty(index, "NAME")
            ?? string.Empty;

        return new WimImageInfo(
            index,
            wim.GetImageName(index) ?? string.Empty,
            wim.GetImageDescription(index) ?? string.Empty,
            displayName,
            wim.GetImageProperty(index, "WINDOWS/EDITIONID") ?? string.Empty,
            wim.GetImageProperty(index, "WINDOWS/INSTALLATIONTYPE") ?? string.Empty,
            wim.GetImageProperty(index, "WINDOWS/ARCH") ?? string.Empty,
            wim.GetImageProperty(index, "WINDOWS/LANGUAGES/DEFAULT") ?? string.Empty,
            long.TryParse(wim.GetImageProperty(index, "TOTALBYTES"), out var totalBytes) ? totalBytes : 0,
            bootIndex == index);
    }

    private static ProgressCallback? CreateProgressCallback(Action<WimOperationProgress>? progress, CancellationToken cancellationToken)
    {
        if (progress is null && !cancellationToken.CanBeCanceled)
        {
            return null;
        }

        return (message, information, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CallbackStatus.Abort;
            }

            progress?.Invoke(CreateProgress(message, information));
            return CallbackStatus.Continue;
        };
    }

    private static WimOperationProgress CreateProgress(ProgressMsg message, object? information)
    {
        return information switch
        {
            ExtractProgress extract => CreateByteProgress(
                WimOperationStage.Extracting,
                extract.CompletedBytes,
                extract.TotalBytes,
                string.IsNullOrWhiteSpace(extract.ImageName) ? extract.Target : extract.ImageName),
            WriteStreamsProgress write => CreateByteProgress(
                WimOperationStage.Writing,
                write.CompletedBytes,
                write.TotalBytes,
                write.CompressionType.ToString()),
            VerifyStreamsProgress verify => CreateByteProgress(
                WimOperationStage.Verifying,
                verify.CurrentBytes,
                verify.TotalBytes,
                verify.WimFile),
            IntegrityProgress integrity => CreateByteProgress(
                WimOperationStage.Verifying,
                integrity.CompletedBytes,
                integrity.TotalBytes,
                integrity.FileName),
            _ => new WimOperationProgress(MapStage(message), null, null, null, null)
        };
    }

    private static WimOperationProgress CreateByteProgress(
        WimOperationStage stage,
        ulong completedBytes,
        ulong totalBytes,
        string? currentItem)
    {
        double? percent = totalBytes == 0
            ? null
            : Math.Clamp(completedBytes * 100.0 / totalBytes, 0, 100);

        return new WimOperationProgress(stage, percent, completedBytes, totalBytes, currentItem);
    }

    private static WimOperationStage MapStage(ProgressMsg message)
    {
        return message switch
        {
            ProgressMsg.ExtractImageBegin or ProgressMsg.ExtractTreeBegin or ProgressMsg.ExtractFileStructure or ProgressMsg.ExtractStreams => WimOperationStage.Extracting,
            ProgressMsg.WriteStreams => WimOperationStage.Writing,
            ProgressMsg.VerifyIntegrity or ProgressMsg.CalcIntegrity or ProgressMsg.VerifyStreams => WimOperationStage.Verifying,
            ProgressMsg.WriteMetadataBegin or ProgressMsg.WriteMetadataEnd or ProgressMsg.ExtractMetadata => WimOperationStage.Metadata,
            ProgressMsg.ExtractImageEnd or ProgressMsg.ExtractTreeEnd or ProgressMsg.DoneWithFile => WimOperationStage.Completed,
            _ => WimOperationStage.Other
        };
    }
}
