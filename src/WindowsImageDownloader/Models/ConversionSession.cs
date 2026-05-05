using System.Diagnostics;
using ManagedWimLib;

namespace WindowsImageDownloader.Models;

internal sealed class ConversionSession
{
    private static readonly TimeSpan _snapshotInterval = TimeSpan.FromMilliseconds(250);

    private readonly EsdToIsoRequest _request;
    private readonly object? _sender;
    private readonly EventHandler<EsdToIsoTaskSnapshot>? _onProgressChanged;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Dictionary<EsdToIsoStage, double> _wimStageHighWaterMarks = [];
    private readonly Dictionary<EsdToIsoStage, HashSet<string>> _metadataItemsByStage = [];

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
        StagingDirectory = request.StagingRoot;
        IsoPath = Path.Combine(
            Path.GetDirectoryName(request.SourceEsdPath)!,
            Path.GetFileNameWithoutExtension(request.SourceEsdPath) + ".iso");
        SourcesDirectory = Path.Combine(StagingDirectory, "sources");
        BootWimPath = Path.Combine(SourcesDirectory, "boot.wim");
        InstallEsdPath = Path.Combine(SourcesDirectory, "install.esd");
    }

    public string TaskId { get; }
    public DateTimeOffset StartedAt { get; }
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
    public CompressionType InstallCompression => _request.InstallCompression;

    public void PublishWimProgress(EsdToIsoStage stage, WimOperationProgress progress)
    {
        Publish(
            EsdToIsoTaskState.Running,
            stage,
            CalculateWimProgress(stage, progress),
            progress.CurrentItem,
            wimProgress: progress,
            force: progress.Stage == WimOperationStage.Completed);
    }

    private double CalculateWimProgress(EsdToIsoStage stage, WimOperationProgress progress)
    {
        var candidate = StageProgress(stage, progress.Stage, progress.Percent);

        if (TryGetMetadataItemProgress(stage, progress, out var metadataProgress))
        {
            candidate = metadataProgress;
        }

        if (_wimStageHighWaterMarks.TryGetValue(stage, out var highWater))
        {
            candidate = Math.Max(candidate, highWater);
        }

        _wimStageHighWaterMarks[stage] = candidate;
        return candidate;
    }

    private bool TryGetMetadataItemProgress(
        EsdToIsoStage stage,
        WimOperationProgress progress,
        out double metadataProgress)
    {
        metadataProgress = 0d;

        if (progress.Stage != WimOperationStage.Metadata || string.IsNullOrWhiteSpace(progress.CurrentItem))
        {
            return false;
        }

        var expectedCount = ExpectedMetadataItemCount(stage);
        if (expectedCount == 0)
        {
            return false;
        }

        if (!_metadataItemsByStage.TryGetValue(stage, out var seenItems))
        {
            seenItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _metadataItemsByStage[stage] = seenItems;
        }

        seenItems.Add(progress.CurrentItem);
        var (start, end) = StageBounds(stage);
        metadataProgress = start + (end - start) * 0.02d * Math.Min(seenItems.Count, expectedCount) / expectedCount;
        return true;
    }

    private int ExpectedMetadataItemCount(EsdToIsoStage stage) => stage switch
    {
        EsdToIsoStage.BuildingBootWim => Images.Count(static image => image.Index is 2 or 3),
        EsdToIsoStage.BuildingInstallImage => Images.Count(static image => image.Index >= 4),
        _ => 0
    };

    public void Publish(
        EsdToIsoTaskState state,
        EsdToIsoStage stage,
        double progress,
        string? currentFile = null,
        string? errorMessage = null,
        WimOperationProgress? wimProgress = null,
        IsoOperationProgress? isoProgress = null,
        DateTimeOffset? completedAt = null,
        bool force = false)
    {
        progress = Math.Clamp(progress, 0, 1);

        if (state == EsdToIsoTaskState.Running && progress < _currentProgress)
        {
            progress = _currentProgress;
        }

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
            currentFile,
            errorMessage,
            IsoPath,
            StartedAt,
            completedAt,
            _stopwatch.Elapsed,
            wimProgress,
            isoProgress));
    }

    public EsdToIsoResult Finish(bool succeeded, string? errorMessage)
    {
        var completedAt = DateTimeOffset.Now;
        var result = new EsdToIsoResult(
            _request.SourceEsdPath,
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

        Publish(
            succeeded ? EsdToIsoTaskState.Completed : EsdToIsoTaskState.Failed,
            succeeded ? EsdToIsoStage.Completed : EsdToIsoStage.Failed,
            succeeded ? 1d : _currentProgress,
            succeeded ? IsoPath : null,
            succeeded ? null : errorMessage,
            completedAt: completedAt,
            force: true);

        return result;
    }

    private static double StageProgress(EsdToIsoStage stage, WimOperationStage wimStage, double? nestedPercent)
    {
        var (start, end) = StageBounds(stage);
        double width = end - start;
        double pct = nestedPercent ?? 0d;

        double inner = stage switch
        {
            EsdToIsoStage.ApplyingSetupMedia => wimStage switch
            {
                WimOperationStage.Extracting => pct / 100d,
                WimOperationStage.Completed => 1.0d,
                _ => 0d
            },
            EsdToIsoStage.BuildingBootWim or EsdToIsoStage.BuildingInstallImage => wimStage switch
            {
                WimOperationStage.Writing => 0.02d + 0.86d * pct / 100d,
                WimOperationStage.Verifying => 0.88d + 0.12d * pct / 100d,
                WimOperationStage.Completed => 1.0d,
                _ => 0d
            },
            _ => 0d
        };

        return Math.Clamp(start + width * inner, start, end);
    }

    private static (double Start, double End) StageBounds(EsdToIsoStage stage) => stage switch
    {
        EsdToIsoStage.ApplyingSetupMedia => (0.08d, 0.30d),
        EsdToIsoStage.BuildingBootWim => (0.30d, 0.50d),
        EsdToIsoStage.BuildingInstallImage => (0.50d, 0.86d),
        _ => (0d, 1d)
    };
}
