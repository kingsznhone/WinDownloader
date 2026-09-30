# WindowsImageDownloader — Copilot 编码上下文

WinUI 3 + .NET 10 应用，负责 ESD 下载、校验、任务缓存和手动 ISO 转换。

修改前阅读 [AGENTS.md](../AGENTS.md)，按 [工作流](../docs/WORKFLOW.md) 定位相关模块。架构、DI、生命周期和存储路径统一维护在 [ARCHITECTURE.md](../docs/ARCHITECTURE.md)，不在本文件重复展开。

## 核心约束

- 路径由 `IDownloadTaskPathService` 解析，下载与校验由 `IEsdDownloadPipeline` 执行，状态由各自 orchestrator 管理。
- 下载按 `MaxConcurrentDownloads` 限流；ISO 转换固定单并发，下载完成后不自动转换。
- ISO 状态通过独立快照通知 UI，不写 SQLite，不新增 `TaskState.Converting` 或 `OutputFormat` 设置。
- 默认 `sources\install.wim` 复用官方 solid LZMS 资源；`WimProcessingService` 只能作为 Singleton。
- `TaskChanged` / `ConversionChanged` 可来自后台线程；UI 集合及绑定属性经 `DispatcherQueue.TryEnqueue` 更新。
- SQLite schema 不兼容或损坏时会重建，任务历史丢失；修改时检查 [schema 联动项](../docs/WORKFLOW.md#sqlite-schema-联动检查)。
- 设置扩展同步接口、服务、ViewModel、XAML 和 [设置文档](../docs/MODULE_SETTINGS.md)；保留已有用户值。
- 语言资源维护 `en-US` / `zh-CN`，正常构建生成 `WinDownloader.pri`，切换语言后重启生效。
- 修改主项目代码后运行工作流规定的 x64 构建；仅修改文档时检查链接和索引。
