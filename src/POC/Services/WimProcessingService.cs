using ManagedWimLib;
using POC.Interfaces;
using POC.Models;
using ManagedWim = ManagedWimLib.Wim;

namespace POC.Services;

public sealed class WimProcessingService : IWimProcessingService, IDisposable
{
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _isDisposed;

    public WimProcessingService()
    {
        var nativeLibraryPath = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "libwim-15.dll");
        if (File.Exists(nativeLibraryPath))
        {
            ManagedWim.GlobalInit(nativeLibraryPath, InitFlags.None);
        }
        else
        {
            ManagedWim.GlobalInit(InitFlags.None);
        }
    }

    public async Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath, nameof(imagePath));
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"文件不存在: {imagePath}", imagePath);

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
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
            throw new FileNotFoundException($"文件不存在: {request.SourceImagePath}", request.SourceImagePath);
        if (request.ImageIndex < 1)
            throw new ArgumentOutOfRangeException(nameof(request.ImageIndex), request.ImageIndex, "WIM 映像索引从 1 开始。");
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationDirectory, nameof(request.DestinationDirectory));

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
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
            throw new FileNotFoundException($"文件不存在: {request.SourceImagePath}", request.SourceImagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationImagePath, nameof(request.DestinationImagePath));

        if (request.Images.Count == 0)
        {
            throw new ArgumentException("至少需要一个待导出的映像。", nameof(request.Images));
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _operationLock.WaitAsync(ct);
        try
        {
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
                ConfigureOutput(destinationWim, request);

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

                var writeFlags = CreateWriteFlags(request);
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
        if (_isDisposed)
        {
            return;
        }

        ManagedWim.TryGlobalCleanup();
        _operationLock.Dispose();
        _isDisposed = true;
    }

    private static WimImageInfo GetImageInfo(ManagedWim wim, uint bootIndex, int index)
    {
        var displayName = GetFirstNonEmpty(wim.GetImageProperty(index, "DISPLAYNAME"), wim.GetImageProperty(index, "NAME"));

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

    private static string GetFirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
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

    private static void ConfigureOutput(ManagedWim wim, WimExportRequest request)
    {
        wim.SetOutputCompressionType(request.Compression);
        wim.SetOutputPackCompressionType(request.Compression);

        if (request.OutputChunkSize > 0)
        {
            wim.SetOutputChunkSize(request.OutputChunkSize);
        }

        if (request.OutputPackChunkSize > 0)
        {
            wim.SetOutputPackChunkSize(request.OutputPackChunkSize);
        }
    }

    private static WriteFlags CreateWriteFlags(WimExportRequest request)
    {
        var flags = WriteFlags.None;

        if (request.CheckIntegrity)
        {
            flags |= WriteFlags.CheckIntegrity;
        }

        if (request.Recompress)
        {
            flags |= WriteFlags.Recompress;
        }

        if (request.Solid)
        {
            flags |= WriteFlags.Solid;
        }

        return flags;
    }

}
