using ManagedWimLib;
using POC.Models;
using WinDownloader.Iso;
using WinDownloader.Iso.Interfaces;
using WinDownloader.Wim;

namespace POC.Services;

public sealed class CliConversionService
{
    private const uint BootChunkSize = 32 * 1024;
    private const uint InstallWimChunkSize = 128 * 1024;

    private readonly IWimProcessingService _wimService;
    private readonly IIsoCreationService _isoService;

    public CliConversionService(IWimProcessingService wimService, IIsoCreationService isoService)
    {
        _wimService = wimService;
        _isoService = isoService;
    }

    public async Task<CliConversionResult> ConvertAsync(
        string esdPath,
        string stagingDirectory,
        string isoPath,
        string volumeLabel,
        bool keepIntermediateFiles,
        CompressionType installCompression,
        bool recompressInstallImage,
        IProgress<CliConversionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(esdPath))
            throw new ArgumentException("ESD path is required.", nameof(esdPath));
        if (!File.Exists(esdPath))
            throw new FileNotFoundException($"ESD not found: {esdPath}", esdPath);
        if (string.IsNullOrWhiteSpace(stagingDirectory))
            throw new ArgumentException("Staging directory is required.", nameof(stagingDirectory));
        if (!recompressInstallImage && installCompression != CompressionType.LZMS)
            throw new ArgumentException("Fast install.wim export requires LZMS compression or force recompression.", nameof(installCompression));

        var startedAt = DateTimeOffset.Now;
        var warnings = new List<string>();
        var sourcesDir = Path.Combine(stagingDirectory, "sources");
        var bootWimPath = Path.Combine(sourcesDir, "boot.wim");
        var installWimPath = Path.Combine(sourcesDir, "install.wim");

        try
        {
            // Preparing
            Report(progress, 0, "Preparing", "正在清理并准备 staging 目录");
            TryDeleteDirectory(stagingDirectory);
            Directory.CreateDirectory(stagingDirectory);
            Directory.CreateDirectory(sourcesDir);

            // InspectingSource
            Report(progress, 0.03, "InspectingSource", "正在读取 ESD 映像信息");
            var images = await _wimService.GetImagesAsync(esdPath, cancellationToken).ConfigureAwait(false);
            ValidateImages(images);

            // ApplyingSetupMedia
            Report(progress, 0.08, "ApplyingSetupMedia", "正在展开 image 1 到 ISO staging");
            await _wimService.ExtractImageAsync(
                new WimExtractRequest(esdPath, 1, stagingDirectory),
                p => Report(progress, MapWimProgress(0.08, 0.30, p), "ApplyingSetupMedia", FormatWim("正在提取 setup 媒体", p)),
                cancellationToken).ConfigureAwait(false);

            // BuildingBootWim
            Report(progress, 0.30, "BuildingBootWim", "正在生成 boot.wim");
            var bootItems = images
                .Where(static i => i.Index is 2 or 3)
                .Select(static i =>
                {
                    var name = !string.IsNullOrWhiteSpace(i.Name) ? i.Name : i.Title;
                    var desc = !string.IsNullOrWhiteSpace(i.Description) ? i.Description : i.Title;
                    return new WimImageExportItem(i.Index, name, desc, i.Index == 3 ? ExportFlags.Boot : ExportFlags.None);
                })
                .ToList();
            await _wimService.ExportImagesAsync(
                new WimExportRequest(esdPath, bootWimPath, bootItems, CompressionType.LZX,
                    CheckIntegrity: true, Recompress: true, Solid: false, OutputChunkSize: BootChunkSize),
                p => Report(progress, MapWimProgress(0.30, 0.50, p), "BuildingBootWim", FormatWim("正在写入 boot.wim", p)),
                cancellationToken).ConfigureAwait(false);

            // BuildingInstallImage
            Report(progress, 0.50, "BuildingInstallImage", "正在生成 install.wim");
            var installItems = images
                .Where(static i => i.Index >= 4)
                .Select(static i =>
                {
                    var name = !string.IsNullOrWhiteSpace(i.Name) ? i.Name : i.Title;
                    var desc = !string.IsNullOrWhiteSpace(i.Description) ? i.Description : i.Title;
                    return new WimImageExportItem(i.Index, name, desc, ExportFlags.None);
                })
                .ToList();
            await _wimService.ExportImagesAsync(
                new WimExportRequest(esdPath, installWimPath, installItems, installCompression,
                    CheckIntegrity: true, Recompress: recompressInstallImage, Solid: true,
                    OutputChunkSize: recompressInstallImage ? InstallWimChunkSize : 0,
                    OutputPackChunkSize: recompressInstallImage ? InstallWimChunkSize : 0),
                p => Report(progress, MapWimProgress(0.50, 0.86, p), "BuildingInstallImage", FormatWim("正在写入 install.wim", p)),
                cancellationToken).ConfigureAwait(false);

            // CreatingIso
            Report(progress, 0.86, "CreatingIso", "正在调用 oscdimg 创建 ISO");
            var isoResult = await _isoService.CreateIsoAsync(
                new IsoCreationRequest(stagingDirectory, isoPath, volumeLabel)
                {
                    OnProgress = p => Report(progress,
                        0.86 + 0.14 * p.Percent / 100.0,
                        "CreatingIso",
                        $"正在创建 ISO {p.Percent:0}%")
                },
                cancellationToken).ConfigureAwait(false);
            warnings.AddRange(isoResult.Warnings);

            if (!isoResult.Succeeded)
                return new CliConversionResult(false, stagingDirectory, bootWimPath, installWimPath, isoPath,
                    DateTimeOffset.Now - startedAt, isoResult.ErrorMessage ?? "ISO creation failed.", warnings);

            Report(progress, 1.0, "Completed", "ESD 到 ISO 转换完成");
            return new CliConversionResult(true, stagingDirectory, bootWimPath, installWimPath, isoPath,
                DateTimeOffset.Now - startedAt, null, warnings);
        }
        finally
        {
            if (!keepIntermediateFiles)
                TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void Report(IProgress<CliConversionProgress>? progress, double pct, string stage, string message)
        => progress?.Report(new CliConversionProgress(Math.Clamp(pct, 0, 1), stage, message));

    private static double MapWimProgress(double start, double end, WimOperationProgress p)
    {
        double inner = p.Stage switch
        {
            WimOperationStage.Extracting => (p.Percent ?? 0) / 100.0,
            WimOperationStage.Writing => 0.02 + 0.86 * (p.Percent ?? 0) / 100.0,
            WimOperationStage.Verifying => 0.88 + 0.12 * (p.Percent ?? 0) / 100.0,
            WimOperationStage.Completed => 1.0,
            _ => 0
        };
        return Math.Clamp(start + (end - start) * inner, start, end);
    }

    private static string FormatWim(string prefix, WimOperationProgress p)
    {
        var pct = p.Percent.HasValue ? $" {p.Percent.Value:0.0}%" : string.Empty;
        var item = !string.IsNullOrWhiteSpace(p.CurrentItem) ? $" ({Path.GetFileName(p.CurrentItem)})" : string.Empty;
        return $"{prefix} [{p.Stage}]{pct}{item}";
    }

    private static void ValidateImages(IReadOnlyList<WimImageInfo> images)
    {
        if (images.Count < 4)
            throw new InvalidOperationException("ESD must contain at least image 1, 2, 3, and one install image.");
        foreach (var idx in new[] { 1, 2, 3, 4 })
            if (images.All(i => i.Index != idx))
                throw new InvalidOperationException($"ESD is missing required image index {idx}.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
