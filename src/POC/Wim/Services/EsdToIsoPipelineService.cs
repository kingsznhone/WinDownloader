using System.Diagnostics;
using POC.Wim.Interfaces;
using POC.Wim.Models;
using static POC.Wim.Services.EsdToIsoOutputWriter;
using static POC.Wim.Services.EsdToIsoPipelinePlanning;
using static POC.Wim.Services.EsdToIsoProgressFactory;

namespace POC.Wim.Services;

public sealed class EsdToIsoPipelineService : IEsdToIsoPipelineService
{
    private readonly IReadOnlyList<IIsoCreationService> _isoCreationServices;
    private readonly IWimProcessingService _wimProcessingService;

    public EsdToIsoPipelineService(
        IWimProcessingService wimProcessingService,
        IEnumerable<IIsoCreationService> isoCreationServices)
    {
        _wimProcessingService = wimProcessingService;
        _isoCreationServices = isoCreationServices.ToList();
    }

    public async Task<EsdToIsoResult> BuildAsync(
        EsdToIsoRequest request,
        IProgress<EsdToIsoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var paths = CreateRunPaths(request);
        Directory.CreateDirectory(paths.RunDirectory);

        using var eventWriter = new EsdToIsoEventWriter(paths.EventsPath);
        var warnings = new List<string>();

        void Report(EsdToIsoProgress value)
        {
            var current = value with { Elapsed = stopwatch.Elapsed };
            eventWriter.Write(current);
            progress?.Report(current);
        }

        try
        {
            Report(CreateProgress(EsdToIsoStage.Preparing, "正在准备输出目录", percent: 0));
            Directory.CreateDirectory(paths.StagingDirectory);

            Report(CreateProgress(EsdToIsoStage.InspectingSource, "正在读取 ESD 映像信息", percent: 3));
            var images = await _wimProcessingService.GetImagesAsync(request.SourceEsdPath, cancellationToken).ConfigureAwait(false);
            ValidateSourceImages(images);

            Report(CreateProgress(EsdToIsoStage.ApplyingSetupMedia, "正在展开 image 1 到 ISO staging", 1, images.Count, paths.StagingDirectory, 8));
            await _wimProcessingService.ExtractImageAsync(
                request.SourceEsdPath,
                1,
                paths.StagingDirectory,
                CreateWimProgress(progress => Report(CreateProgress(
                    EsdToIsoStage.ApplyingSetupMedia,
                    progress.Message,
                    1,
                    images.Count,
                    progress.CurrentItem,
                    StagePercent(EsdToIsoStage.ApplyingSetupMedia, progress.Percent),
                    progress))),
                cancellationToken).ConfigureAwait(false);

            var sourcesDirectory = Path.Combine(paths.StagingDirectory, "sources");
            Directory.CreateDirectory(sourcesDirectory);

            var bootWimPath = Path.Combine(sourcesDirectory, "boot.wim");
            Report(CreateProgress(EsdToIsoStage.BuildingBootWim, "正在生成 boot.wim", 2, images.Count, bootWimPath, 30));
            await BuildBootWimAsync(request.SourceEsdPath, bootWimPath, images, Report, cancellationToken).ConfigureAwait(false);

            var installImagePaths = new List<string>();
            foreach (var installTarget in GetInstallTargets(request.InstallFormat, sourcesDirectory))
            {
                Report(CreateProgress(EsdToIsoStage.BuildingInstallImage, $"正在生成 {Path.GetFileName(installTarget.Path)}", 4, images.Count, installTarget.Path, 55));
                await BuildInstallImageAsync(request.SourceEsdPath, installTarget, images, Report, cancellationToken).ConfigureAwait(false);
                installImagePaths.Add(installTarget.Path);
                AddFileSizeWarnings(installTarget.Path, warnings);
            }

            var isoResults = new List<IsoCreationResult>();
            foreach (var backend in ExpandBackends(request.IsoBackend))
            {
                var isoPath = Path.Combine(paths.RunDirectory, $"{Path.GetFileNameWithoutExtension(request.SourceEsdPath)}-{backend.ToString().ToLowerInvariant()}.iso");
                var isoRequest = new IsoCreationRequest(paths.StagingDirectory, isoPath, request.VolumeLabel, backend);
                var result = await CreateIsoAsync(backend, isoRequest, Report, cancellationToken).ConfigureAwait(false);
                isoResults.Add(result);
                warnings.AddRange(result.Warnings);
            }

            Report(CreateProgress(EsdToIsoStage.WritingManifest, "正在写入 manifest 和摘要", percent: 97));
            var completedAt = DateTimeOffset.Now;
            var resultModel = new EsdToIsoResult(
                request.SourceEsdPath,
                paths.RunDirectory,
                paths.StagingDirectory,
                bootWimPath,
                installImagePaths,
                isoResults,
                images,
                warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                paths.EventsPath,
                paths.ManifestPath,
                paths.SummaryPath,
                startedAt,
                completedAt);

            await WriteManifestAsync(resultModel, cancellationToken).ConfigureAwait(false);
            await WriteSummaryAsync(resultModel, cancellationToken).ConfigureAwait(false);
            Report(CreateProgress(EsdToIsoStage.Completed, "ESD 到 ISO POC 流程完成", percent: 100));
            return resultModel;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Report(CreateProgress(EsdToIsoStage.Failed, ex.Message, percent: null));
            throw;
        }
    }

    private async Task BuildBootWimAsync(
        string sourceEsdPath,
        string bootWimPath,
        IReadOnlyList<WimImageInfo> images,
        Action<EsdToIsoProgress> report,
        CancellationToken cancellationToken)
    {
        var items = images
            .Where(static image => image.Index is 2 or 3)
            .Select(static image => CreateExportItem(image, image.Index == 3))
            .ToList();

        var request = new WimMultiImageExportRequest(
            sourceEsdPath,
            bootWimPath,
            items,
            WimCompressionKind.LZX,
            CheckIntegrity: true,
            Recompress: true,
            Solid: false,
            OutputChunkSize: BootChunkSize);

        await _wimProcessingService.ExportImagesAsync(
            request,
            CreateWimProgress(progress => report(CreateProgress(
                EsdToIsoStage.BuildingBootWim,
                progress.Message,
                null,
                images.Count,
                progress.CurrentItem,
                StagePercent(EsdToIsoStage.BuildingBootWim, progress.Percent),
                progress))),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task BuildInstallImageAsync(
        string sourceEsdPath,
        InstallTarget installTarget,
        IReadOnlyList<WimImageInfo> images,
        Action<EsdToIsoProgress> report,
        CancellationToken cancellationToken)
    {
        var items = images
            .Where(static image => image.Index >= 4)
            .Select(static image => CreateExportItem(image, markBootable: false))
            .ToList();

        var request = new WimMultiImageExportRequest(
            sourceEsdPath,
            installTarget.Path,
            items,
            installTarget.Compression,
            CheckIntegrity: true,
            Recompress: true,
            Solid: installTarget.Solid,
            OutputChunkSize: installTarget.ChunkSize,
            OutputPackChunkSize: installTarget.PackChunkSize);

        await _wimProcessingService.ExportImagesAsync(
            request,
            CreateWimProgress(progress => report(CreateProgress(
                EsdToIsoStage.BuildingInstallImage,
                progress.Message,
                null,
                images.Count,
                progress.CurrentItem,
                StagePercent(EsdToIsoStage.BuildingInstallImage, progress.Percent),
                progress))),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationBackend backend,
        IsoCreationRequest request,
        Action<EsdToIsoProgress> report,
        CancellationToken cancellationToken)
    {
        var service = _isoCreationServices.FirstOrDefault(service => service.Backend == backend);
        if (service is null)
        {
            return IsoCreationResult.Skip(backend, request.OutputIsoPath, $"未注册 {backend} ISO 后端。");
        }

        report(CreateProgress(EsdToIsoStage.CreatingIso, $"正在创建 {backend} ISO", backend: backend, currentFile: request.OutputIsoPath, percent: 86));

        return await service.CreateIsoAsync(
            request,
            new ActionProgress<EsdToIsoProgress>(progress => report(progress with
            {
                Percent = progress.Percent ?? StagePercent(EsdToIsoStage.CreatingIso, null)
            })),
            cancellationToken).ConfigureAwait(false);
    }
}
