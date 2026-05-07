# WindowsImageDownloader — Agent 工作指南

> 本文档是 AI Agent 的入口指南。在修改代码前，先阅读本文件，再按任务范围阅读 `docs/` 下的模块文档。

## 项目简介

WindowsImageDownloader 是一个基于 WinUI 3 + .NET 10 的 Windows 安装映像下载工具。主应用当前负责：从 Microsoft Update Catalog 获取产品目录、筛选 ESD 文件、多线程断点续传下载、SHA-256 校验、SQLite 任务持久化、WinUI 下载任务管理，以及下载完成后的可选 ESD 到 ISO 转换。

ISO 转换已接入主项目：`src/WinDownloader.Wim` 封装 ManagedWimLib，`src/WinDownloader.Iso` 是纯 ISO 打包库（oscdimg 封装）；ESD→ISO 流水线编排（`EsdToIsoConversionService`）在主应用内，POC 有平行的 `CliConversionService`。WinUI 与 POC 共同引用两个共享库。默认安装映像输出为复用官方 solid LZMS 资源的 `sources\install.wim`。主项目不引入 `OutputFormat` 设置，不把 ISO 转换写入 SQLite schema，不扩展下载任务 `TaskState` 为 `Converting`；转换进度通过独立 `EsdToIsoTaskSnapshot` 传递给 UI。

## 文档体系

| 文档 | 内容 |
|------|------|
| [WORKFLOW.md](docs/WORKFLOW.md) | 标准工作流程，先读 |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | 总体架构、技术栈、DI、数据流、服务层评估 |
| [MODULE_CATALOG.md](docs/MODULE_CATALOG.md) | 产品目录获取 |
| [MODULE_DOWNLOAD.md](docs/MODULE_DOWNLOAD.md) | ESD 下载、SHA-256 校验、SQLite 缓存、下载任务编排 |
| [MODULE_CONVERSION.md](docs/MODULE_CONVERSION.md) | 用户手动触发的 ESD 到 ISO 转换调度、转换流水线、`.staging`、active count |
| [MODULE_WIM.md](docs/MODULE_WIM.md) | `WinDownloader.Wim` 共享库：ManagedWimLib 封装、接口、模型 |
| [MODULE_ISO.md](docs/MODULE_ISO.md) | `WinDownloader.Iso` 共享库：oscdimg 封装、接口、模型 |
| [MODULE_MODELS.md](docs/MODULE_MODELS.md) | 数据模型 |
| [MODULE_UI.md](docs/MODULE_UI.md) | WinUI 页面、控件、ViewModel |
| [MODULE_SETTINGS.md](docs/MODULE_SETTINGS.md) | 设置服务和设置页 |
| [MODULE_LOCALIZATION.md](docs/MODULE_LOCALIZATION.md) | WinUI/MRT Core 本地化、`.resw`、PRI 和语言重启生效 |
| [MODULE_PACKAGING.md](docs/MODULE_PACKAGING.md) | 打包与发布 |
| [MODULE_POC.md](docs/MODULE_POC.md) | POC 控制台宿主和 WIM/ISO 共享库验证 |

## 快速避坑

| 注意事项 | 说明 |
|----------|------|
| ISO 转换边界 | 主应用已有 ISO 转换入口，但不要顺手加入格式选择、转换持久化或新的 task state，除非先重新设计 |
| Downloader 包装 | `DownloadService` 可注册为 Singleton；每次 `DownloadAsync` 内部创建独立 Downloader 实例 |
| UI 线程 | `ObservableCollection` 和 XAML 绑定更新必须经 `DispatcherQueue.TryEnqueue` |
| 任务并发 | ESD 下载按 `MaxConcurrentDownloads` 限流；ISO 转换固定单并发 |
| Active 计数 | 下载页聚合下载编排和 ISO 编排的 `ActiveTaskCount`，用于徽标 |
| SQLite schema | 下载任务 schema 不保存 ISO 转换状态；不兼容或损坏时自动删除重建 |
| 路径职责 | ESD、ISO 和 `.staging` 路径由 `IDownloadTaskPathService` 统一解析 |
| 校验职责 | ESD 下载与 SHA-256 校验由 `IEsdDownloadPipeline` 封装 |
| WIM 生命周期 | `WinDownloader.Wim.WimProcessingService` 是 Singleton，内部串行化 WIM 操作并在 dispose 时清理 ManagedWimLib 全局状态 |
| 设置扩展 | `IAppSettings` → `AppSettingsService` → ViewModel → XAML → 文档 |
| 本地化 | 当前只维护 `en-US`/`zh-CN`，正常构建自动生成 `WinDownloader.pri`，切换语言后重启生效 |
