# WindowsImageDownloader — Agent 工作指南

> 本文档是 AI Agent 的入口指南。在修改代码前，先阅读本文件，再按任务范围阅读 `docs/` 下的模块文档。

## 项目简介

WindowsImageDownloader 是一个基于 WinUI 3 + .NET 10 的 Windows 安装映像下载工具。主应用当前负责：从 Microsoft Update Catalog 获取产品目录、筛选 ESD 文件、多线程断点续传下载、SHA-256 校验、SQLite 任务持久化、WinUI 下载任务管理，以及下载完成后的可选 ESD 到 ISO 转换。

ISO 转换已经接入主项目：ManagedWimLib 负责 ESD/WIM 操作，`Oscdimg` 工具目录随主项目复制到输出目录。`src/POC` 仍保留为控制台验证和对照宿主。主项目当前不引入 `OutputFormat` 设置，不把 ISO 转换写入 SQLite schema，也不扩展下载任务 `TaskState` 为 `Converting`；转换进度通过独立 `EsdToIsoTaskSnapshot` 传递给 UI。

## 文档体系

| 文档 | 内容 |
|------|------|
| [WORKFLOW.md](docs/WORKFLOW.md) | 标准工作流程，先读 |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | 总体架构、技术栈、DI、数据流、服务层评估 |
| [MODULE_CATALOG.md](docs/MODULE_CATALOG.md) | 产品目录获取 |
| [MODULE_DOWNLOAD.md](docs/MODULE_DOWNLOAD.md) | ESD 下载、ISO 转换调度、缓存、任务编排 |
| [MODULE_MODELS.md](docs/MODULE_MODELS.md) | 数据模型 |
| [MODULE_UI.md](docs/MODULE_UI.md) | WinUI 页面、控件、ViewModel |
| [MODULE_SETTINGS.md](docs/MODULE_SETTINGS.md) | 设置服务和设置页 |
| [MODULE_PACKAGING.md](docs/MODULE_PACKAGING.md) | 打包与发布 |
| [MODULE_POC.md](docs/MODULE_POC.md) | POC 项目和 WIM/ISO 验证区 |

## 快速避坑

| 注意事项 | 说明 |
|----------|------|
| ISO 转换边界 | 主应用已有 ISO 转换入口，但不要顺手加入格式选择、转换持久化或新的 task state，除非先重新设计 |
| Downloader 包装 | `DownloadService` 可注册为 Singleton；每次 `DownloadAsync` 内部创建独立 Downloader 实例 |
| UI 线程 | `ObservableCollection` 和 XAML 绑定更新必须经 `DispatcherQueue.TryEnqueue` |
| 任务并发 | ESD 下载按 `MaxConcurrentDownloads` 限流；ISO 转换固定单并发 |
| Active 计数 | `ActiveTaskCount` 合并下载 worker 和 ISO 转换 worker，用于下载页徽标 |
| SQLite schema | 下载任务 schema 不保存 ISO 转换状态；不兼容或损坏时自动删除重建 |
| 路径职责 | ESD、ISO 和 `.staging` 路径由 `IDownloadTaskPathService` 统一解析 |
| 校验职责 | ESD 下载与 SHA-256 校验由 `IEsdDownloadPipeline` 封装 |
| WIM 生命周期 | `WimProcessingService` 是 Singleton，内部串行化 WIM 操作并在 dispose 时清理 ManagedWimLib 全局状态 |
| 设置扩展 | `IAppSettings` → `AppSettingsService` → ViewModel → XAML → 文档 |
