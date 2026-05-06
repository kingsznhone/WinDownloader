# WindowsImageDownloader — 总体架构

## 项目概述

WindowsImageDownloader 是基于 WinUI 3 (Windows App SDK) 和 .NET 10 的 Windows 安装映像下载工具。主应用从 Microsoft Update Catalog 获取产品目录，筛选 ESD 文件，多线程断点续传下载，完成后执行 SHA-256 校验，并提供可选的 ESD 到 ISO 转换。

ISO 转换已经是主项目能力：`WindowsImageDownloader.Wim` 负责读取、提取和导出 ESD/WIM 映像，`WindowsImageDownloader.Iso` 负责 ESD→ISO 流水线和随输出复制的 `Oscdimg` 工具调用。默认安装映像输出为复用官方 solid LZMS 资源的 `sources\install.wim`。转换任务不写入 SQLite，不扩展下载任务 `TaskState`；UI 通过独立 `EsdToIsoTaskSnapshot` 显示转换状态。

## 技术栈

| 层级 | 技术 |
|------|------|
| UI 框架 | WinUI 3 (`Microsoft.UI.Xaml`) |
| 运行时 | .NET 10 + Windows App SDK 2.0 |
| MVVM | CommunityToolkit.Mvvm |
| DI / 生命周期 | Microsoft.Extensions.Hosting + Microsoft.Extensions.DependencyInjection |
| 下载引擎 | Downloader NuGet |
| ISO 转换 | `WindowsImageDownloader.Wim` + `WindowsImageDownloader.Iso` + bundled Oscdimg |
| 数据库 | Microsoft.Data.Sqlite |
| 设置存储 | JSON 文件 |
| 打包 | 非 MSIX 解包部署 (`WindowsPackageType=None`) |

## 解决方案结构

```text
src/
├── WindowsImageDownloader/             # 主 WinUI 应用，ESD 下载 + ISO 转换
│   ├── App.xaml / App.xaml.cs          # Host、DI、应用生命周期
│   ├── MainWindow.xaml / .cs           # NavigationView 导航壳
│   ├── Interfaces/                     # 下载、缓存、路径、WIM/ISO 服务接口
│   ├── Models/                         # 目录、下载任务、ISO/WIM、UI 快照模型
│   ├── Oscdimg/                        # ISO 创建工具 oscdimg.exe，复制到输出目录
│   ├── Services/                       # 服务实现
│   ├── ViewModels/                     # MVVM ViewModel
│   ├── Views/                          # Pages + Controls
│   └── Assets/
├── WindowsImageDownloader.Wim/         # ManagedWimLib 封装库
├── WindowsImageDownloader.Iso/         # ESD→ISO 流水线和 oscdimg 后端库
├── POC/                                # 控制台验证/对照宿主
│   ├── Program.cs
│   └── Oscdimg/
└── WindowsImageDownloader.slnx
```

## 主项目关键文件

| 区域 | 文件 | 说明 |
|------|------|------|
| 接口 | `Interfaces/IUpdateCatalogService.cs` | 产品目录获取契约 |
| 接口 | `Interfaces/IDownloadService.cs` | 底层 HTTP 下载契约和 `DownloadProgress` |
| 接口 | `Interfaces/IDownloadTaskPathService.cs` | ESD、ISO、`.staging` 任务路径解析契约 |
| 接口 | `Interfaces/IEsdDownloadPipeline.cs` | ESD 下载与校验契约 |
| 共享库 | `WindowsImageDownloader.Wim/IWimProcessingService.cs` | ManagedWimLib 操作契约 |
| 共享库 | `WindowsImageDownloader.Iso/IEsdToIsoConversionService.cs` | ESD 到 ISO 转换契约 |
| 共享库 | `WindowsImageDownloader.Iso/IIsoCreationService.cs` | ISO 创建后端契约 |
| 接口 | `Interfaces/ICacheService.cs` | SQLite 缓存契约 |
| 接口 | `Interfaces/ITaskOrchestratorService.cs` | 下载和 ISO 转换调度契约 |
| 服务 | `Services/UpdateCatalogService.cs` | Microsoft Update Catalog 客户端 |
| 服务 | `Services/DownloadService.cs` | Downloader 包装 |
| 服务 | `Services/DownloadTaskPathService.cs` | 下载目录、ESD、ISO、临时路径解析 |
| 服务 | `Services/EsdDownloadPipeline.cs` | 调用下载服务并校验 SHA-256 |
| 共享库 | `WindowsImageDownloader.Iso/Services/EsdToIsoConversionService.cs` | ESD 到 ISO 产品层转换流水线 |
| 共享库 | `WindowsImageDownloader.Wim/Services/WimProcessingService.cs` | ManagedWimLib 单例包装，串行化 WIM 操作 |
| 共享库 | `WindowsImageDownloader.Iso/Services/OscdimgIsoCreationService.cs` | 调用 `oscdimg.exe` 创建 ISO，并解析进度 |
| 服务 | `Services/CacheService.cs` | SQLite 持久化和 schema 自恢复 |
| 服务 | `Services/TaskOrchestratorService.cs` | 下载调度、ISO 转换调度、快照转发、active count |
| 模型 | `Models/DownloadTask.cs` | ESD 下载任务模型 |
| 模型 | `Models/TaskState.cs` | `Queued`/`Downloading`/`Verifying`/`Completed`/`Failed` |
| 共享库 | `WindowsImageDownloader.Iso/Models/EsdToIsoTaskSnapshot.cs` | ISO 转换状态、阶段和进度快照 |

## DI 注册

```text
IAppSettings                 Singleton  AppSettingsService
IUpdateCatalogService        Singleton  UpdateCatalogService
ICacheService                Singleton  CacheService
IDownloadService             Singleton  DownloadService
IDownloadTaskPathService     Singleton  DownloadTaskPathService
IEsdDownloadPipeline         Singleton  EsdDownloadPipeline
IWimProcessingService        Singleton  WimProcessingService
IIsoCreationService          Singleton  OscdimgIsoCreationService
IEsdToIsoConversionService   Singleton  EsdToIsoConversionService
ITaskOrchestratorService     Singleton  TaskOrchestratorService
SelectionViewModel           Singleton
SettingsViewModel            Singleton
DownloadPageViewModel        Singleton

HostedService: CacheService
HostedService: TaskOrchestratorService
```

启动顺序：`CacheService.StartAsync()` 先确保 SQLite schema，随后 `TaskOrchestratorService.StartAsync()` 加载持久化任务并恢复中断下载状态。停止顺序相反，Host 关闭时先让 `TaskOrchestratorService` 取消 worker，再释放服务。

## 数据流

### 下载

```text
SelectionPage
  → SelectionViewModel.EnsureCatalogLoadedAsync
  → UpdateCatalogService.GetCatalogAsync
  → products.cab 下载/校验/解压
  → products.xml 解析为 RawFile 列表
  → RawFileGroup 分组展示
  → 用户点击下载
  → DownloadTask.FromRawFile
  → TaskOrchestratorService.EnqueueAsync
  → CacheService.AddTaskAsync
  → TaskOrchestratorService.ScheduleDownloadAsync
  → EsdDownloadPipeline.DownloadAsync
  → DownloadService.DownloadAsync
  → EsdDownloadPipeline.VerifyAsync
  → TaskChanged 快照通知 UI
```

### ISO 转换

```text
DownloadTaskItemViewModel.ConvertToIsoAsync
  → TaskOrchestratorService.ConvertToIsoAsync
  → 单并发 ISO conversion worker
  → EsdToIsoConversionService.ConvertAsync
  → WimProcessingService.GetImagesAsync
  → WimProcessingService.ExtractImageAsync image 1 到 .staging
  → WimProcessingService.ExportImagesAsync image 2+3 到 boot.wim
  → WimProcessingService.ExportImagesAsync image 4..n 到 install.wim（默认复用官方 solid LZMS 资源）
  → OscdimgIsoCreationService.CreateIsoAsync
  → EsdToIsoTaskSnapshot 合并到 DownloadTaskSnapshot
  → DownloadTaskItemViewModel 显示主进度和子进度
```

已有最终 ISO 时，`ConvertToIsoCommand` 直接打开 ISO 所在目录，不再排队转换。

## 线程模型

| 线程 | 角色 |
|------|------|
| UI 线程 | WinUI 可视化树、Frame 导航、`ObservableCollection` 添加/删除、绑定属性更新 |
| ThreadPool | 下载执行、SHA-256 文件校验、ISO 转换 worker、任务状态持久化 |
| HostedService 生命周期 | 启动时建表/加载任务，关闭时取消下载和 ISO 转换 worker |

重要约束：

- `TaskChanged` 可从后台线程触发，ViewModel 必须通过 `DispatcherQueue.TryEnqueue` 应用快照。
- `_taskMap` 使用 `ConcurrentDictionary`；`_tasks` 是 UI 绑定集合，只在 UI 线程添加/删除。
- 下载并发由 `TaskOrchestratorService` 在任务启动前读取 `MaxConcurrentDownloads` 控制。
- ISO 转换并发固定为 1，通过独立 conversion slot 控制。
- `ActiveTaskCount` 合并下载 worker 和 ISO 转换 worker，用于下载页徽标。
- `WimProcessingService` 是 Singleton，内部用 `SemaphoreSlim(1, 1)` 串行化 ManagedWimLib 操作，并在 dispose 时调用 `ManagedWim.TryGlobalCleanup()`。

## 服务层组织评估

服务职责整体仍然清晰：`DownloadService` 只包装 Downloader，`EsdDownloadPipeline` 只负责下载和校验，`TaskOrchestratorService` 只调度转换 worker；`EsdToIsoConversionService`、`WimProcessingService` 和 `OscdimgIsoCreationService` 已剥离到共享库，分别封装转换流水线、WIM/ESD 原语和 ISO 后端。

当前唯一明显偏重的文件是 `TaskOrchestratorService.cs`：它同时承担 ESD 下载 worker、ISO 转换 worker、快照转发、active count、缓存同步和任务集合事件。短期保持集中是合理的，因为下载页 task item 的生命周期入口都汇聚在这里；如果以后加入 ISO 取消按钮、恢复、重试、转换持久化或多队列策略，应拆出独立的 ISO 转换编排服务，再由当前 orchestrator 汇总 UI 快照。

## 关闭生命周期

主窗口关闭时，`App.OnMainWindowClosing` 会取消窗口关闭，创建 15 秒 `CancellationTokenSource` 并调用 `_host.StopAsync(cts.Token)`。`TaskOrchestratorService.StopAsync` 会取消 `_shutdownCts`，再等待下载 worker 和 ISO worker 结束。

ISO 转换被取消时的处理是尽力而为：

- ISO worker 发布 `Canceled` 快照并释放 conversion slot。
- `EsdToIsoConversionService` 在 `finally` 中按 `KeepIntermediateFiles=false` 尝试删除 `.staging`。
- `OscdimgIsoCreationService` 在取消时 kill `oscdimg.exe` 进程树。
- 转换任务不持久化，重启后不会自动恢复。
- 如果底层文件仍被占用或进程退出较慢，可能留下 `.staging` 或半成品 ISO；下次转换开始时会重新清理 `.staging`。

## 数据持久化

| 存储 | 位置 | 用途 |
|------|------|------|
| SQLite | `%LocalAppData%\WindowsImageDownloader\cache.db` | 下载任务持久化；不保存 ISO 转换状态 |
| JSON | `%LocalAppData%\WindowsImageDownloader\settings.json` | 应用设置 |
| CAB 缓存 | `%LocalAppData%\WindowsImageDownloader\catalog_cache\` | 产品目录 CAB 和 XML |
| ISO 输出 | `{DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\{FileNameWithoutExtension}.iso` | ESD 转换成品 |
| ISO staging | `{DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\.staging` | 转换中间文件，默认完成后删除 |

## 模块依赖矩阵

| 模块 | 依赖 | 被依赖 |
|------|------|--------|
| UpdateCatalogService | HttpClient, expand.exe, SHA256, XML/JSON parser | SelectionViewModel |
| DownloadService | Downloader, IAppSettings | EsdDownloadPipeline |
| DownloadTaskPathService | IAppSettings | EsdDownloadPipeline, TaskOrchestratorService, DownloadTaskItemViewModel |
| EsdDownloadPipeline | IDownloadService, IDownloadTaskPathService, SHA256 | TaskOrchestratorService |
| WindowsImageDownloader.Iso | WindowsImageDownloader.Wim, ManagedWimLib, bundled Oscdimg | POC, WindowsImageDownloader |
| WindowsImageDownloader.Wim | ManagedWimLib | WindowsImageDownloader.Iso, POC, WindowsImageDownloader |
| CacheService | Microsoft.Data.Sqlite | TaskOrchestratorService, Host |
| TaskOrchestratorService | ICacheService, IEsdDownloadPipeline, IEsdToIsoConversionService, IDownloadTaskPathService, IAppSettings | SelectionViewModel, DownloadPageViewModel, DownloadTaskItemViewModel |
| AppSettingsService | JSON, UserDataPaths | DownloadService, path service, settings UI |

## POC 边界

`src/POC` 是控制台验证和对照宿主。它不再保留独立 WIM/ISO 模型和服务，而是直接引用 `WindowsImageDownloader.Wim` 与 `WindowsImageDownloader.Iso`，用于验证共享转换库、进度映射、压缩参数和 oscdimg 输出解析；主项目拥有正式 WinUI 入口和服务注册。POC 行为可以更偏诊断，不能自动视为主应用产品承诺。
