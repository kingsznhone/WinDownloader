# WindowsImageDownloader — Agent 工作指南

> 本文档是 AI Agent 的入口指南。在修改代码前，先阅读本文件，再按任务范围阅读 `docs/` 下的模块文档。

## 项目简介

WindowsImageDownloader 是一个基于 WinUI 3 + .NET 10 的 Windows 安装映像下载工具。主应用当前只负责：从 Microsoft Update Catalog 获取产品目录、筛选 ESD 文件、多线程断点续传下载、SHA-256 校验、SQLite 任务持久化和 WinUI 下载任务管理。

WIM/ISO 后处理已从主项目剥离到 `src/POC`，用于后续概念验证。主项目不应重新引入 `ManagedWimLib`、`DiscUtils`、`OutputFormat` 或 WIM/ISO 转换状态，除非用户明确要求重新设计该功能。

## 文档体系

| 文档 | 内容 |
|------|------|
| [WORKFLOW.md](docs/WORKFLOW.md) | 标准工作流程，先读 |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | 总体架构、技术栈、DI、数据流 |
| [MODULE_CATALOG.md](docs/MODULE_CATALOG.md) | 产品目录获取 |
| [MODULE_DOWNLOAD.md](docs/MODULE_DOWNLOAD.md) | ESD 下载管道、缓存、任务编排 |
| [MODULE_MODELS.md](docs/MODULE_MODELS.md) | 数据模型 |
| [MODULE_UI.md](docs/MODULE_UI.md) | WinUI 页面、控件、ViewModel |
| [MODULE_SETTINGS.md](docs/MODULE_SETTINGS.md) | 设置服务和设置页 |
| [MODULE_PACKAGING.md](docs/MODULE_PACKAGING.md) | 打包与发布 |
| [MODULE_POC.md](docs/MODULE_POC.md) | POC 项目和 WIM/ISO 实验区 |

## 快速避坑

| 注意事项 | 说明 |
|----------|------|
| 主应用 ESD-only | 不在主项目中添加 WIM/ISO 转换代码或格式状态 |
| Downloader 包装 | `DownloadService` 可注册为 Singleton；每次 `DownloadAsync` 内部创建独立 Downloader 实例 |
| UI 线程 | `ObservableCollection` 和 XAML 绑定更新必须经 `DispatcherQueue.TryEnqueue` |
| 任务并发 | `TaskOrchestratorService` 使用 `SemaphoreSlim` 按 `MaxConcurrentDownloads` 限流 |
| SQLite schema | 不兼容或损坏时自动删除重建；用户已接受缓存重建 |
| 路径职责 | 下载路径由 `IDownloadTaskPathService` 统一解析 |
| 校验职责 | ESD 下载与 SHA-256 校验由 `IEsdDownloadPipeline` 封装 |
| 设置扩展 | `IAppSettings` → `AppSettingsService` → ViewModel → XAML → 文档 |
