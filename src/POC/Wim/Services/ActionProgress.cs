namespace POC.Wim.Services;

internal sealed class ActionProgress<T> : IProgress<T>
{
    private readonly Action<T> _report;

    public ActionProgress(Action<T> report)
    {
        _report = report;
    }

    public void Report(T value) => _report(value);
}
