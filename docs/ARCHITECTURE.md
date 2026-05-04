# WindowsImageDownloader — 总体架构

## 项目概述

WindowsImageDownloader 是基于 WinUI 3 (Windows App SDK) 和 .NET 10 的 Windows 安装映像下载工具。主应用当前聚焦 ESD 下载：从 Microsoft Update Catalog 获取产品目录，筛选 ESD 文件，多线程断点续传下载，并在完成后执行 SHA-256 校验。

WIM/ISO 后处理已迁移到 `src/POC`。主应用不再包含格式选择、转换管道、WIM/ISO 状态或 ManagedWimLib/DiscUtils 依赖。

## 技术栈

| 层级 | 技术 |
|------|------|
| UI 框架 | WinUI 3 (`Microsoft.UI.Xaml`) |
| 运行时 | .NET 10 + Windows App SDK 2.0 |
| MVVM | CommunityToolkit.Mvvm |
| DI / 生命周期 | Microsoft.Extensions.Hosting + Microsoft.Extensions.DependencyInjection |
| 下载引擎 | Downloader NuGet |
| 数据库 | Microsoft.Data.Sqlite |
| 设置存储 | JSON 文件 |
| 打包 | 非 MSIX 解包部署 (`WindowsPackageType=None`) |
| POC 后处理 | ManagedWimLib、DiscUtils |

## 解决方案结构

```text
src/
├── WindowsImageDownloader/             # 主 WinUI 应用，ESD-only
│   ├── App.xaml / App.xaml.cs          # Host、DI、应用生命周期
│   ├── MainWindow.xaml / .cs           # NavigationView 导航壳
│   ├── Interfaces/                     # 服务接口
│   ├── Models/                         # ESD 下载、目录、UI 模型
│   ├── Services/                       # 服务实现
│   ├── ViewModels/                     # MVVM ViewModel
│   ├── Views/                          # Pages + Controls
│   └── Assets/
├── POC/                                # WIM/ISO 后处理概念验证
│   └── Wim/
└── WindowsImageDownloader.slnx
```

## 主项目关键文件

| 区域 | 文件 | 说明 |
|------|------|------|
| 接口 | `Interfaces/IUpdateCatalogService.cs` | 产品目录获取契约 |
| 接口 | `Interfaces/IDownloadService.cs` | 底层 HTTP 下载契约和 `DownloadProgress` |
| 接口 | `Interfaces/IDownloadTaskPathService.cs` | ESD 任务路径解析契约 |
| 接口 | `Interfaces/IEsdDownloadPipeline.cs` | ESD 下载与校验契约 |
| 接口 | `Interfaces/ICacheService.cs` | SQLite 缓存契约 |
| 接口 | `Interfaces/ITaskOrchestratorService.cs` | 下载任务编排契约 |
| 服务 | `Services/UpdateCatalogService.cs` | Microsoft Update Catalog 客户端 |
| 服务 | `Services/DownloadService.cs` | Downloader 包装 |
| 服务 | `Services/DownloadTaskPathService.cs` | 下载目录、ESD 路径、临时路径解析 |
| 服务 | `Services/EsdDownloadPipeline.cs` | 调用下载服务并校验 SHA-256 |
| 服务 | `Services/CacheService.cs` | SQLite 持久化和 schema 自恢复 |
| 服务 | `Services/TaskOrchestratorService.cs` | 任务调度、暂停/恢复/取消/删除 |
| 模型 | `Models/DownloadTask.cs` | ESD 下载任务模型 |
| 模型 | `Models/TaskState.cs` | `Queued`/`Downloading`/`Verifying`/`Completed`/`Failed` |

## DI 注册

```text
IAppSettings              Singleton  AppSettingsService
IUpdateCatalogService     Singleton  UpdateCatalogService
ICacheService             Singleton  CacheService
IDownloadService          Singleton  DownloadService
IDownloadTaskPathService  Singleton  DownloadTaskPathService
IEsdDownloadPipeline      Singleton  EsdDownloadPipeline
ITaskOrchestratorService  Singleton  TaskOrchestratorService
SelectionViewModel        Singleton
SettingsViewModel         Singleton
DownloadPageViewModel     Singleton

HostedService: CacheService
HostedService: TaskOrchestratorService
```

启动顺序：`CacheService.StartAsync()` 先确保 SQLite schema，随后 `TaskOrchestratorService.StartAsync()` 加载持久化任务并恢复中断任务状态。

## 数据流

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

## 线程模型

| 线程 | 角色 |
|------|------|
| UI 线程 | WinUI 可视化树、Frame 导航、`ObservableCollection` 添加/删除 |
| ThreadPool | 下载执行、SHA-256 文件校验、任务状态持久化 |
| HostedService 生命周期 | 启动时建表/加载任务，关闭时取消活动下载 |

重要约束：

- `TaskChanged` 可从后台线程触发，ViewModel 必须通过 `DispatcherQueue.TryEnqueue` 应用快照。
- `_taskMap` 使用 `ConcurrentDictionary`；`_tasks` 是 UI 绑定集合，只在 UI 线程添加/删除。
- 下载并发由 `TaskOrchestratorService` 在任务启动前读取 `MaxConcurrentDownloads` 控制。
- 调高 `MaxConcurrentDownloads` 会影响后续排队任务；调低不会中断已运行下载，但会限制新的任务启动。

## 数据持久化

| 存储 | 位置 | 用途 |
|------|------|------|
| SQLite | `%LocalAppData%\WindowsImageDownloader\cache.db` | 下载任务持久化 |
| JSON | `%LocalAppData%\WindowsImageDownloader\settings.json` | 应用设置 |
| CAB 缓存 | `%LocalAppData%\WindowsImageDownloader\catalog_cache\` | 产品目录 CAB 和 XML |

## 模块依赖矩阵

| 模块 | 依赖 | 被依赖 |
|------|------|--------|
| UpdateCatalogService | HttpClient, expand.exe, SHA256, XML/JSON parser | SelectionViewModel |
| DownloadService | Downloader, IAppSettings | EsdDownloadPipeline |
| DownloadTaskPathService | IAppSettings | EsdDownloadPipeline, TaskOrchestratorService, DownloadTaskItemViewModel |
| EsdDownloadPipeline | IDownloadService, IDownloadTaskPathService, SHA256 | TaskOrchestratorService |
| CacheService | Microsoft.Data.Sqlite | TaskOrchestratorService, Host |
| TaskOrchestratorService | ICacheService, IEsdDownloadPipeline, IDownloadTaskPathService, IAppSettings | SelectionViewModel, DownloadPageViewModel |
| AppSettingsService | JSON, UserDataPaths | DownloadService, path service, settings UI |

## POC 边界

`src/POC` 是后处理实验区。它可以引用 ManagedWimLib 和 DiscUtils，也可以探索 ESD/WIM/ISO 转换流程；这些依赖和模型不属于主 WinUI 应用的运行路径。
