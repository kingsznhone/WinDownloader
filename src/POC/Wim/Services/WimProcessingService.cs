using ManagedWimLib;
using POC.Wim.Interfaces;
using POC.Wim.Models;
using System.Runtime.InteropServices;
using ManagedWim = ManagedWimLib.Wim;

namespace POC.Wim.Services;

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
            return new WimLibraryInfo(ManagedWim.VersionStr);
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

                using var wim = ManagedWim.OpenWim(imagePath, OpenFlags.None);
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

                using var wim = ManagedWim.OpenWim(imagePath, OpenFlags.None);
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
                using var sourceWim = ManagedWim.OpenWim(request.SourceImagePath, OpenFlags.None);
                using var destinationWim = ManagedWim.CreateNewWim(MapCompression(request.Compression));

                if (callback is not null)
                {
                    sourceWim.RegisterCallback(callback);
                    destinationWim.RegisterCallback(callback);
                }

                var exportFlags = request.MarkBootable ? ExportFlags.Boot : ExportFlags.None;
                var writeFlags = request.CheckIntegrity ? WriteFlags.CheckIntegrity : WriteFlags.None;
                sourceWim.ExportImage(request.ImageIndex, destinationWim, request.ImageName, request.ImageDescription, exportFlags);
                destinationWim.Write(request.DestinationImagePath, ManagedWim.AllImages, writeFlags, 0);
                progress?.Report(new WimOperationProgress(WimOperationStage.Completed, "导出完成", 100, null, null, request.DestinationImagePath));
            }, cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ExportImagesAsync(
        WimMultiImageExportRequest request,
        IProgress<WimOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateExistingFile(request.SourceImagePath, nameof(request.SourceImagePath));
        ValidateFilePath(request.DestinationImagePath, nameof(request.DestinationImagePath));

        if (request.Images.Count == 0)
        {
            throw new ArgumentException("至少需要一个待导出的映像。", nameof(request.Images));
        }

        foreach (var image in request.Images)
        {
            ValidateImageIndex(image.ImageIndex);
        }

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

                if (File.Exists(request.DestinationImagePath))
                {
                    File.Delete(request.DestinationImagePath);
                }

                var callback = CreateProgressCallback(progress, cancellationToken);
                using var sourceWim = ManagedWim.OpenWim(request.SourceImagePath, OpenFlags.None);
                using var destinationWim = ManagedWim.CreateNewWim(MapCompression(request.Compression));
                ConfigureOutput(destinationWim, request);

                if (callback is not null)
                {
                    sourceWim.RegisterCallback(callback);
                    destinationWim.RegisterCallback(callback);
                }

                foreach (var image in request.Images)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var exportFlags = image.MarkBootable ? ExportFlags.Boot : ExportFlags.None;
                    sourceWim.ExportImage(
                        image.ImageIndex,
                        destinationWim,
                        image.ImageName,
                        image.ImageDescription,
                        exportFlags);

                    progress?.Report(new WimOperationProgress(
                        WimOperationStage.Metadata,
                        $"已加入映像 {image.ImageIndex}",
                        null,
                        null,
                        null,
                        image.ImageName));
                }

                var writeFlags = CreateWriteFlags(request);
                destinationWim.Write(request.DestinationImagePath, ManagedWim.AllImages, writeFlags, 0);
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
                ManagedWim.TryGlobalCleanup();
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

            var nativeLibraryPath = ResolvePackagedNativeLibraryPath();
            if (nativeLibraryPath is not null)
            {
                ManagedWim.GlobalInit(nativeLibraryPath, InitFlags.None);
            }
            else
            {
                ManagedWim.GlobalInit(InitFlags.None);
            }

            _isInitialized = true;
        }
    }

    private static string? ResolvePackagedNativeLibraryPath()
    {
        var runtimeIdentifier = GetNativeRuntimeIdentifier();
        if (runtimeIdentifier is null)
        {
            return null;
        }

        var libraryFileName = GetNativeLibraryFileName();
        if (libraryFileName is null)
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", runtimeIdentifier, "native", libraryFileName);
        return File.Exists(path) ? path : null;
    }

    private static string? GetNativeRuntimeIdentifier()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => null
        };

        if (architecture is null)
        {
            return null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return architecture == "arm" ? null : $"win-{architecture}";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return architecture is "x64" or "arm64" ? $"osx-{architecture}" : null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return architecture == "x86" ? null : $"linux-{architecture}";
        }

        return null;
    }

    private static string? GetNativeLibraryFileName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "libwim-15.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libwim.dylib";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "libwim.so";
        }

        return null;
    }

    private static WimImageInfo CreateImageInfo(ManagedWim wim, WimInfo wimInfo, int index)
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

    private static string GetOptionalImageProperty(ManagedWim wim, int imageIndex, params string[] propertyNames)
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

    private static long GetOptionalLongImageProperty(ManagedWim wim, int imageIndex, params string[] propertyNames)
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

    private static void RegisterProgressCallback(ManagedWim wim, IProgress<WimOperationProgress>? progress, CancellationToken cancellationToken)
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

    private static void ConfigureOutput(ManagedWim wim, WimMultiImageExportRequest request)
    {
        var compression = MapCompression(request.Compression);
        wim.SetOutputCompressionType(compression);
        wim.SetOutputPackCompressionType(compression);

        if (request.OutputChunkSize > 0)
        {
            wim.SetOutputChunkSize(request.OutputChunkSize);
        }

        if (request.OutputPackChunkSize > 0)
        {
            wim.SetOutputPackChunkSize(request.OutputPackChunkSize);
        }
    }

    private static WriteFlags CreateWriteFlags(WimMultiImageExportRequest request)
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
