using ManagedWimLib;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Services;

public sealed class WimProcessingService : IWimProcessingService, IDisposable
{
    private readonly Lock _initializationLock = new();
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _isDisposed;
    private bool _isInitialized;

    public async Task<WimLibraryInfo> GetLibraryInfoAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureInitialized();
            return new WimLibraryInfo(Wim.VersionStr);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        ValidateExistingFile(imagePath, nameof(imagePath));

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureInitialized();

                using var wim = Wim.OpenWim(imagePath, OpenFlags.None);
                var wimInfo = wim.GetWimInfo();
                var images = new List<WimImageInfo>((int)wimInfo.ImageCount);

                for (var index = 1; index <= wimInfo.ImageCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    images.Add(CreateImageInfo(wim, wimInfo, index));
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
        string imagePath,
        int imageIndex,
        string destinationDirectory,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateExistingFile(imagePath, nameof(imagePath));
        ValidateImageIndex(imageIndex);
        ValidateDirectoryPath(destinationDirectory, nameof(destinationDirectory));

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureInitialized();
                Directory.CreateDirectory(destinationDirectory);

                using var wim = Wim.OpenWim(imagePath, OpenFlags.None);
                RegisterProgressCallback(wim, progress, cancellationToken);
                wim.ExtractImage(imageIndex, destinationDirectory, ExtractFlags.None);
                progress?.Report(new WimOperationProgress(WimOperationStage.Completed, "提取完成", 100, null, null, destinationDirectory));
            }, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ExportImageAsync(
        WimExportRequest request,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateExistingFile(request.SourceImagePath, nameof(request.SourceImagePath));
        ValidateImageIndex(request.ImageIndex);
        ValidateFilePath(request.DestinationImagePath, nameof(request.DestinationImagePath));

        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureInitialized();

                var destinationDirectory = Path.GetDirectoryName(request.DestinationImagePath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                var callback = CreateProgressCallback(progress, cancellationToken);
                using var sourceWim = Wim.OpenWim(request.SourceImagePath, OpenFlags.None);
                using var destinationWim = Wim.CreateNewWim(MapCompression(request.Compression));

                if (callback is not null)
                {
                    sourceWim.RegisterCallback(callback);
                    destinationWim.RegisterCallback(callback);
                }

                var exportFlags = request.MarkBootable ? ExportFlags.Boot : ExportFlags.None;
                var writeFlags = request.CheckIntegrity ? WriteFlags.CheckIntegrity : WriteFlags.None;
                sourceWim.ExportImage(request.ImageIndex, destinationWim, request.ImageName, request.ImageDescription, exportFlags);
                destinationWim.Write(request.DestinationImagePath, 0, writeFlags, 0);
                progress?.Report(new WimOperationProgress(WimOperationStage.Completed, "导出完成", 100, null, null, request.DestinationImagePath));
            }, cancellationToken);
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

        lock (_initializationLock)
        {
            if (_isInitialized)
            {
                Wim.TryGlobalCleanup();
                _isInitialized = false;
            }
        }

        _operationLock.Dispose();
        _isDisposed = true;
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_initializationLock)
        {
            if (_isInitialized)
            {
                return;
            }

            Wim.GlobalInit(InitFlags.None);
            _isInitialized = true;
        }
    }

    private static WimImageInfo CreateImageInfo(Wim wim, WimInfo wimInfo, int index)
    {
        var name = GetOptionalValue(() => wim.GetImageName(index));
        var description = GetOptionalValue(() => wim.GetImageDescription(index));
        var displayName = GetOptionalImageProperty(wim, index, "DISPLAYNAME", "NAME");

        return new WimImageInfo(
            index,
            name,
            description,
            displayName,
            GetOptionalImageProperty(wim, index, "EDITIONID", "WINDOWS/EDITIONID"),
            GetOptionalImageProperty(wim, index, "INSTALLATIONTYPE", "WINDOWS/INSTALLATIONTYPE"),
            GetOptionalImageProperty(wim, index, "ARCH", "WINDOWS/ARCH"),
            GetOptionalImageProperty(wim, index, "DEFAULTLANGUAGE", "WINDOWS/LANGUAGES/DEFAULT"),
            GetOptionalLongImageProperty(wim, index, "TOTALBYTES"),
            wimInfo.BootIndex == index);
    }

    private static string GetOptionalImageProperty(Wim wim, int imageIndex, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetOptionalValue(() => wim.GetImageProperty(imageIndex, propertyName));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static long GetOptionalLongImageProperty(Wim wim, int imageIndex, params string[] propertyNames)
    {
        var value = GetOptionalImageProperty(wim, imageIndex, propertyNames);
        return long.TryParse(value, out var result) ? result : 0;
    }

    private static string GetOptionalValue(Func<string?> valueFactory)
    {
        try
        {
            return valueFactory() ?? string.Empty;
        }
        catch (WimLibException)
        {
            return string.Empty;
        }
    }

    private static void RegisterProgressCallback(Wim wim, IProgress<WimOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var callback = CreateProgressCallback(progress, cancellationToken);
        if (callback is not null)
        {
            wim.RegisterCallback(callback);
        }
    }

    private static ProgressCallback? CreateProgressCallback(IProgress<WimOperationProgress>? progress, CancellationToken cancellationToken)
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

            progress?.Report(CreateProgress(message, information));
            return CallbackStatus.Continue;
        };
    }

    private static WimOperationProgress CreateProgress(ProgressMsg message, object? information)
    {
        return information switch
        {
            ExtractProgress extract => CreateByteProgress(
                WimOperationStage.Extracting,
                "正在提取映像",
                extract.CompletedBytes,
                extract.TotalBytes,
                string.IsNullOrWhiteSpace(extract.ImageName) ? extract.Target : extract.ImageName),
            WriteStreamsProgress write => CreateByteProgress(
                WimOperationStage.Writing,
                "正在写入映像",
                write.CompletedBytes,
                write.TotalBytes,
                write.CompressionType.ToString()),
            VerifyStreamsProgress verify => CreateByteProgress(
                WimOperationStage.Verifying,
                "正在校验数据流",
                verify.CurrentBytes,
                verify.TotalBytes,
                verify.WimFile),
            IntegrityProgress integrity => CreateByteProgress(
                WimOperationStage.Verifying,
                message == ProgressMsg.CalcIntegrity ? "正在计算完整性" : "正在校验完整性",
                integrity.CompletedBytes,
                integrity.TotalBytes,
                integrity.FileName),
            _ => new WimOperationProgress(MapStage(message), message.ToString(), null, null, null, null)
        };
    }

    private static WimOperationProgress CreateByteProgress(
        WimOperationStage stage,
        string message,
        ulong completedBytes,
        ulong totalBytes,
        string? currentItem)
    {
        double? percent = totalBytes == 0
            ? null
            : Math.Clamp(completedBytes * 100.0 / totalBytes, 0, 100);

        return new WimOperationProgress(stage, message, percent, completedBytes, totalBytes, currentItem);
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

    private static CompressionType MapCompression(WimCompressionKind compression)
    {
        return compression switch
        {
            WimCompressionKind.None => CompressionType.None,
            WimCompressionKind.XPRESS => CompressionType.XPRESS,
            WimCompressionKind.LZX => CompressionType.LZX,
            WimCompressionKind.LZMS => CompressionType.LZMS,
            _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, null)
        };
    }

    private static void ValidateExistingFile(string path, string parameterName)
    {
        ValidateFilePath(path, parameterName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"文件不存在: {path}", path);
        }
    }

    private static void ValidateFilePath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("路径不能为空。", parameterName);
        }
    }

    private static void ValidateDirectoryPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("目录不能为空。", parameterName);
        }
    }

    private static void ValidateImageIndex(int imageIndex)
    {
        if (imageIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(imageIndex), imageIndex, "WIM 映像索引从 1 开始。");
        }
    }
}
