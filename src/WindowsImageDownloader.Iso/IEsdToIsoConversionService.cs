namespace WindowsImageDownloader.Iso;

/// <summary>
/// ESD 到 ISO 转换服务。
/// 适合注册为 DI 单例；<see cref="ConvertAsync"/> 在服务级别不持有可变状态，可安全被 ViewModel 调用。
/// </summary>
/// <remarks>
/// <see cref="ProgressChanged"/> 事件可能在线程池线程上触发；WinUI ViewModel 必须通过
/// <c>DispatcherQueue.TryEnqueue</c> 将快照切回 UI 线程后更新绑定属性。
/// </remarks>
public interface IEsdToIsoConversionService
{
    /// <summary>
    /// 转换进度变化时触发。可能在任意线程上触发。
    /// </summary>
    event EventHandler<EsdToIsoTaskSnapshot>? ProgressChanged;

    /// <summary>
    /// 异步执行 ESD 到 ISO 的完整转换流水线。
    /// </summary>
    Task<EsdToIsoResult> ConvertAsync(
        EsdToIsoRequest request,
        CancellationToken cancellationToken = default);
}
