using System.Diagnostics;
using ManagedWimLib;
using POC.Interfaces;
using POC.Models;

namespace POC.Services;

public sealed class EsdToIsoConversionService : IEsdToIsoConversionService
{
    private const uint BootChunkSize = 32 * 1024;
    private const uint InstallEsdChunkSize = 128 * 1024;

    private readonly IIsoCreationService _isoCreationService;
    private readonly IWimProcessingService _wimProcessingService;

    public EsdToIsoConversionService(
        IWimProcessingService wimProcessingService,
        IIsoCreationService isoCreationService)
    {
        _wimProcessingService = wimProcessingService;
        _isoCreationService = isoCreationService;
    }

    public event EventHandler<EsdToIsoTaskSnapshot>? ProgressChanged;

    public async Task<EsdToIsoResult> ConvertAsync(
        EsdToIsoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var session = new ConversionSession(request, this, ProgressChanged);

        try
        {
            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.Preparing, "正在准备输出目录", 0, session.RunDirectory, force: true);
            Directory.CreateDirectory(session.StagingDirectory);
            Directory.CreateDirectory(session.SourcesDirectory);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.InspectingSource, "正在读取 ESD 映像信息", 0.03, request.SourceEsdPath, force: true);
            session.Images = await _wimProcessingService.GetImagesAsync(request.SourceEsdPath, cancellationToken).ConfigureAwait(false);
            ValidateSourceImages(session.Images);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.ApplyingSetupMedia, "正在展开 image 1 到 ISO staging", 0.08, session.StagingDirectory, force: true);
            await _wimProcessingService.ExtractImageAsync(
                new WimExtractRequest(request.SourceEsdPath, 1, session.StagingDirectory),
                progress => session.Publish(
                    EsdToIsoTaskState.Running,
                    EsdToIsoStage.ApplyingSetupMedia,
                    WimStageStatusText(progress.Stage),
                    StageProgress(EsdToIsoStage.ApplyingSetupMedia, progress.Percent),
                    progress.CurrentItem,
                    wimProgress: progress),
                cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.BuildingBootWim, "正在生成 boot.wim", 0.30, session.BootWimPath, force: true);
            await BuildBootWimAsync(request.SourceEsdPath, session, cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.BuildingInstallImage, "正在生成 install.esd", 0.50, session.InstallEsdPath, force: true);
            await BuildInstallEsdAsync(request.SourceEsdPath, session, cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.CreatingIso, "正在调用 oscdimg 创建 ISO", 0.86, session.IsoPath, force: true);
            session.IsoResult = await _isoCreationService.CreateIsoAsync(
                new IsoCreationRequest(session.StagingDirectory, session.IsoPath, request.VolumeLabel),
                cancellationToken).ConfigureAwait(false);
            session.Warnings.AddRange(session.IsoResult.Warnings);

            if (!session.IsoResult.Succeeded)
                return session.Finish(false, session.IsoResult.ErrorMessage ?? "ISO 创建失败。");

            return session.Finish(true, null);
        }
        catch (OperationCanceledException)
        {
            var completedAt = DateTimeOffset.Now;
            session.Publish(
                EsdToIsoTaskState.Canceled,
                session.CurrentStage,
                "ESD 到 ISO 转换已取消",
                session.CurrentProgress,
                errorMessage: "操作已取消。",
                completedAt: completedAt,
                force: true);
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                return session.Finish(false, ex.Message);
            }
            catch
            {
                session.Publish(EsdToIsoTaskState.Failed, EsdToIsoStage.Failed, ex.Message, session.CurrentProgress, errorMessage: ex.Message, force: true);
                throw;
            }
        }
    }

    private async Task BuildBootWimAsync(
        string sourceEsdPath,
        ConversionSession session,
        CancellationToken cancellationToken)
    {
        var items = session.Images
            .Where(static image => image.Index is 2 or 3)
            .Select(static image => CreateExportItem(image, image.Index == 3 ? ExportFlags.Boot : ExportFlags.None))
            .ToList();

        var request = new WimExportRequest(
            sourceEsdPath,
            session.BootWimPath,
            items,
            CompressionType.LZX,
            CheckIntegrity: true,
            Recompress: true,
            Solid: false,
            OutputChunkSize: BootChunkSize);

        await _wimProcessingService.ExportImagesAsync(
            request,
            progress => session.Publish(
                EsdToIsoTaskState.Running,
                EsdToIsoStage.BuildingBootWim,
                WimStageStatusText(progress.Stage),
                StageProgress(EsdToIsoStage.BuildingBootWim, progress.Percent),
                progress.CurrentItem,
                wimProgress: progress),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task BuildInstallEsdAsync(
        string sourceEsdPath,
        ConversionSession session,
        CancellationToken cancellationToken)
    {
        var items = session.Images
            .Where(static image => image.Index >= 4)
            .Select(static image => CreateExportItem(image, ExportFlags.None))
            .ToList();

        var request = new WimExportRequest(
            sourceEsdPath,
            session.InstallEsdPath,
            items,
            CompressionType.LZMS,
            CheckIntegrity: true,
            Recompress: true,
            Solid: true,
            OutputChunkSize: InstallEsdChunkSize,
            OutputPackChunkSize: InstallEsdChunkSize);

        await _wimProcessingService.ExportImagesAsync(
            request,
            progress => session.Publish(
                EsdToIsoTaskState.Running,
                EsdToIsoStage.BuildingInstallImage,
                WimStageStatusText(progress.Stage),
                StageProgress(EsdToIsoStage.BuildingInstallImage, progress.Percent),
                progress.CurrentItem,
                wimProgress: progress),
            cancellationToken).ConfigureAwait(false);
    }

    private static WimImageExportItem CreateExportItem(WimImageInfo image, ExportFlags exportFlags)
    {
        var name = !string.IsNullOrWhiteSpace(image.Name) ? image.Name : image.Title;
        var description = !string.IsNullOrWhiteSpace(image.Description) ? image.Description : image.Title;
        return new WimImageExportItem(image.Index, name, description, exportFlags);
    }

    private static string WimStageStatusText(WimOperationStage stage) => stage switch
    {
        WimOperationStage.Extracting => "正在提取映像",
        WimOperationStage.Writing => "正在写入映像",
        WimOperationStage.Verifying => "正在校验数据流",
        WimOperationStage.Metadata => "正在处理元数据",
        WimOperationStage.Completed => "完成",
        _ => "处理中"
    };

    private static double StageProgress(EsdToIsoStage stage, double? nestedPercent)
    {
        var (start, end) = stage switch
        {
            EsdToIsoStage.ApplyingSetupMedia => (0.08d, 0.30d),
            EsdToIsoStage.BuildingBootWim => (0.30d, 0.50d),
            EsdToIsoStage.BuildingInstallImage => (0.50d, 0.85d),
            EsdToIsoStage.CreatingIso => (0.85d, 0.96d),
            _ => (0d, 1d)
        };

        return nestedPercent is null
            ? start
            : Math.Clamp(start + (end - start) * nestedPercent.Value / 100d, start, end);
    }

    private static void ValidateRequest(EsdToIsoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEsdPath))
        {
            throw new ArgumentException("源 ESD 路径不能为空。", nameof(request));
        }

        if (!File.Exists(request.SourceEsdPath))
        {
            throw new FileNotFoundException($"源 ESD 不存在: {request.SourceEsdPath}", request.SourceEsdPath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            throw new ArgumentException("输出根目录不能为空。", nameof(request));
        }
    }

    private static void ValidateSourceImages(IReadOnlyList<WimImageInfo> images)
    {
        if (images.Count < 4)
        {
            throw new InvalidOperationException("ESD 至少需要包含 image 1、2、3 和一个安装映像。");
        }

        foreach (var requiredIndex in new[] { 1, 2, 3, 4 })
        {
            if (images.All(image => image.Index != requiredIndex))
            {
                throw new InvalidOperationException($"ESD 缺少必需映像 index {requiredIndex}。");
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class ConversionSession
    {
        private static readonly TimeSpan _snapshotInterval = TimeSpan.FromMilliseconds(250);

        private readonly EsdToIsoRequest _request;
        private readonly object? _sender;
        private readonly EventHandler<EsdToIsoTaskSnapshot>? _onProgressChanged;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        private EsdToIsoStage _currentStage;
        private double _currentProgress;
        private DateTimeOffset _lastPublishedAt = DateTimeOffset.MinValue;
        private double _lastPublishedProgress = -1d;
        private EsdToIsoStage? _lastPublishedStage;

        public ConversionSession(
            EsdToIsoRequest request,
            object? sender,
            EventHandler<EsdToIsoTaskSnapshot>? onProgressChanged)
        {
            _request = request;
            _sender = sender;
            _onProgressChanged = onProgressChanged;
            StartedAt = DateTimeOffset.Now;
            TaskId = Path.GetFileNameWithoutExtension(request.SourceEsdPath);
            var runName = $"{TaskId}-{StartedAt:yyyyMMdd-HHmmss}";
            RunDirectory = Path.Combine(request.OutputRoot, runName);
            StagingDirectory = Path.Combine(RunDirectory, "staging");
            IsoPath = Path.Combine(RunDirectory, "oscdimg.iso");
            SourcesDirectory = Path.Combine(StagingDirectory, "sources");
            BootWimPath = Path.Combine(SourcesDirectory, "boot.wim");
            InstallEsdPath = Path.Combine(SourcesDirectory, "install.esd");
        }

        public string TaskId { get; }
        public DateTimeOffset StartedAt { get; }
        public string RunDirectory { get; }
        public string StagingDirectory { get; }
        public string SourcesDirectory { get; }
        public string BootWimPath { get; }
        public string InstallEsdPath { get; }
        public string IsoPath { get; }
        public IReadOnlyList<WimImageInfo> Images { get; set; } = [];
        public IsoCreationResult? IsoResult { get; set; }
        public List<string> Warnings { get; } = [];
        public EsdToIsoStage CurrentStage => _currentStage;
        public double CurrentProgress => _currentProgress;

        public void Publish(
            EsdToIsoTaskState state,
            EsdToIsoStage stage,
            string statusText,
            double progress,
            string? currentFile = null,
            string? errorMessage = null,
            WimOperationProgress? wimProgress = null,
            DateTimeOffset? completedAt = null,
            bool force = false)
        {
            progress = Math.Clamp(progress, 0, 1);
            _currentStage = stage;
            _currentProgress = progress;

            var now = DateTimeOffset.Now;
            var stageChanged = _lastPublishedStage != stage;
            var progressChanged = Math.Abs(progress - _lastPublishedProgress) >= 0.005;
            var terminal = state is EsdToIsoTaskState.Completed or EsdToIsoTaskState.Failed or EsdToIsoTaskState.Canceled;

            if (!force && !terminal && !stageChanged && !progressChanged && now - _lastPublishedAt < _snapshotInterval)
                return;

            _lastPublishedStage = stage;
            _lastPublishedProgress = progress;
            _lastPublishedAt = now;

            _onProgressChanged?.Invoke(_sender, new EsdToIsoTaskSnapshot(
                TaskId,
                _request.SourceEsdPath,
                state,
                stage,
                progress,
                statusText,
                currentFile,
                errorMessage,
                IsoPath,
                StartedAt,
                completedAt,
                _stopwatch.Elapsed,
                wimProgress));
        }

        public EsdToIsoResult Finish(bool succeeded, string? errorMessage)
        {
            var completedAt = DateTimeOffset.Now;
            var result = new EsdToIsoResult(
                _request.SourceEsdPath,
                RunDirectory,
                StagingDirectory,
                BootWimPath,
                InstallEsdPath,
                IsoPath,
                IsoResult,
                Images,
                Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                succeeded,
                errorMessage,
                StartedAt,
                completedAt);

            Directory.CreateDirectory(RunDirectory);

            if (succeeded && !_request.KeepIntermediateFiles)
                EsdToIsoConversionService.TryDeleteDirectory(StagingDirectory);

            Publish(
                succeeded ? EsdToIsoTaskState.Completed : EsdToIsoTaskState.Failed,
                succeeded ? EsdToIsoStage.Completed : EsdToIsoStage.Failed,
                succeeded ? "ESD 到 ISO 转换完成" : errorMessage ?? "ESD 到 ISO 转换失败",
                succeeded ? 1 : _currentProgress,
                succeeded ? IsoPath : null,
                succeeded ? null : errorMessage,
                completedAt: completedAt,
                force: true);

            return result;
        }
    }
}
