using POC.Models;

namespace POC.Services;

public sealed class ConsoleProgressReporter : IProgress<EsdToIsoProgress>
{
    private readonly Lock _lock = new();
    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;
    private int? _lastPercentBucket;
    private EsdToIsoStage? _lastStage;

    public void Report(EsdToIsoProgress value)
    {
        lock (_lock)
        {
            int? percentBucket = value.Percent is null ? null : (int)Math.Floor(value.Percent.Value);
            var shouldWrite = value.Stage != _lastStage ||
                percentBucket != _lastPercentBucket ||
                value.Stage is EsdToIsoStage.Completed or EsdToIsoStage.Failed ||
                DateTimeOffset.Now - _lastWrite > TimeSpan.FromSeconds(2);

            if (!shouldWrite)
            {
                return;
            }

            _lastStage = value.Stage;
            _lastPercentBucket = percentBucket;
            _lastWrite = DateTimeOffset.Now;

            Console.WriteLine(Format(value));
        }
    }

    private static string Format(EsdToIsoProgress progress)
    {
        var percent = progress.Percent is null ? " --.-%" : $"{progress.Percent.Value,5:0.0}%";
        var elapsed = progress.Elapsed.ToString(@"hh\:mm\:ss");
        var backend = progress.Backend is null ? string.Empty : $" [{progress.Backend}]";
        var file = string.IsNullOrWhiteSpace(progress.CurrentFile) ? string.Empty : $" ({Path.GetFileName(progress.CurrentFile)})";
        var nested = progress.WimProgress?.Percent is null ? string.Empty : $" WIM {progress.WimProgress.Percent.Value:0.0}%";
        return $"{elapsed} {percent} {progress.Stage}{backend}: {progress.Message}{nested}{file}";
    }
}
