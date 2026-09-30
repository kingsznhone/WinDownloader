# WindowsImageDownloader — 总体架构

## 项目概述

WindowsImageDownloader 是基于 WinUI 3 (Windows App SDK) 和 .NET 10 的 Windows 安装映像下载工具。主应用从 Microsoft Update Catalog 获取产品目录，筛选 ESD 文件，多线程断点续传下载，完成后执行 SHA-256 校验，并提供可选的 ESD 到 ISO 转换。

`WinDownloader.Wim` 封装 ESD/WIM 操作，`WinDownloader.Iso` 封装 oscdimg，主应用的 `EsdToIsoConversionService` 编排转换流水线。下载任务持久化，ISO 转换通过独立快照通知 UI，不写入 SQLite。

## 技术栈

| 层级 | 技术 |
|------|------|
| UI 框架 | WinUI 3 (`Microsoft.UI.Xaml`) |
| 运行时 | .NET 10 + Microsoft.WindowsAppSDK 2.5.1 |
| MVVM | CommunityToolkit.Mvvm |
| DI / 生命周期 | Microsoft.Extensions.Hosting + Microsoft.Extensions.DependencyInjection |
| 下载引擎 | Downloader NuGet |
| ISO 转换 | `WinDownloader.Wim` + `WinDownloader.Iso` + bundled Oscdimg |
| 数据库 | Microsoft.Data.Sqlite |
| 设置存储 | JSON 文件 |
| 打包 | 非 MSIX 解包部署 (`WindowsPackageType=None`) |

## 解决方案结构

```text
src/
├── WinDownloader/                      # 主 WinUI 应用，ESD 下载 + ISO 转换
│   ├── App.xaml / App.xaml.cs          # Host、DI、应用生命周期
│   ├── MainWindow.xaml / .cs           # NavigationView 导航壳
│   ├── Interfaces/                     # 下载、缓存、路径、转换服务接口
│   ├── Models/                         # 目录、下载任务、ISO/WIM、UI 快照模型
│   ├── Services/                       # 服务实现（含 EsdToIsoConversionService）
│   ├── ViewModels/                     # MVVM ViewModel
│   ├── Views/                          # Pages + Controls
│   └── Assets/
├── WinDownloader.Wim/               # ManagedWimLib 封装库（见 docs/MODULE_WIM.md）
├── WinDownloader.Iso/               # 纯 ISO 打包库，oscdimg 封装（见 docs/MODULE_ISO.md）
├── POC/                             # 控制台验证/对照宿主
│   ├── Program.cs
│   └── Oscdimg/
└── WinDownloader.slnx
```

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
IEsdToIsoOrchestratorService Singleton  EsdToIsoOrchestratorService
IDownloadTaskOrchestratorService Singleton  DownloadTaskOrchestratorService
SelectionViewModel           Singleton
SettingsViewModel            Singleton
DownloadPageViewModel        Singleton

HostedService: CacheService
HostedService: DownloadTaskOrchestratorService
HostedService: EsdToIsoOrchestratorService
```

`AddHostedService` 通过 `sp.GetRequiredService<T>()` 复用已注册的 Singleton，避免同一服务创建两份实例。

启动顺序：`CacheService.StartAsync()` 先确保 SQLite schema，随后 `DownloadTaskOrchestratorService.StartAsync()` 加载持久化任务并恢复中断下载状态，最后启动 no-op 的 `EsdToIsoOrchestratorService`。停止顺序相反，Host 关闭时先取消 ISO 转换 worker，再取消下载 worker。

## 数据流

### 下载

```text
SelectionPage
  → UpdateCatalogService 获取并解析产品目录
  → SelectionViewModel 筛选、分组
  → 用户点击下载
  → DownloadTaskOrchestratorService.EnqueueAsync
  → CacheService 持久化任务
  → EsdDownloadPipeline 下载、SHA-256 校验
  → TaskChanged → DownloadTaskItemViewModel
```

### ISO 转换

```text
DownloadTaskItemViewModel.ConvertToIsoAsync
  → EsdToIsoOrchestratorService.ConvertToIsoAsync
  → 单并发 ISO conversion worker
  → EsdToIsoConversionService.ConvertAsync
  → WimProcessingService 准备安装媒体、boot.wim 和 install.wim
  → OscdimgIsoCreationService.CreateIsoAsync
  → ConversionChanged → DownloadTaskItemViewModel
```

步骤及状态规则见 [下载模块](MODULE_DOWNLOAD.md) 和 [转换模块](MODULE_CONVERSION.md)。

## 线程模型

| 线程 | 角色 |
|------|------|
| UI 线程 | WinUI 可视化树、Frame 导航、`ObservableCollection` 添加/删除、绑定属性更新 |
| ThreadPool | 下载执行、SHA-256 文件校验、ISO 转换 worker、任务状态持久化 |
| HostedService 生命周期 | 启动时建表/加载任务，关闭时取消下载和 ISO 转换 worker |

重要约束：

- `TaskChanged` 可从后台线程触发，ViewModel 必须通过 `DispatcherQueue.TryEnqueue` 应用快照。
- `_taskMap` 使用 `ConcurrentDictionary`；`_tasks` 是 UI 绑定集合，只在 UI 线程添加/删除。
- 下载并发由 `DownloadTaskOrchestratorService` 在任务启动前读取 `MaxConcurrentDownloads` 控制。
- ISO 转换并发由 `EsdToIsoOrchestratorService` 固定为 1；转换是 CPU 和 I/O 密集任务，不读取应用设置。
- 下载页徽标由 `DownloadPageViewModel` 聚合下载 active count 和 ISO active count。
- `WimProcessingService` 是 Singleton，内部用 `SemaphoreSlim(1, 1)` 串行化 ManagedWimLib 操作，并在 dispose 时调用 `ManagedWim.TryGlobalCleanup()`。

## 关闭生命周期

主窗口关闭时，`App.OnMainWindowClosing` 会取消窗口关闭，创建 15 秒 `CancellationTokenSource` 并调用 `_host.StopAsync(cts.Token)`。Host 以注册顺序的反向停止 hosted services：先让 `EsdToIsoOrchestratorService.StopAsync` 取消并等待 ISO worker，再让 `DownloadTaskOrchestratorService.StopAsync` 取消并等待下载 worker。

ISO 取消会终止 oscdimg 并尽力清理 `.staging`，重启后不恢复转换。取消及残留文件规则见 [转换模块](MODULE_CONVERSION.md#应用退出和清理)。

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
| DownloadTaskPathService | IAppSettings | EsdDownloadPipeline, DownloadTaskOrchestratorService, EsdToIsoOrchestratorService, DownloadTaskItemViewModel |
| EsdDownloadPipeline | IDownloadService, IDownloadTaskPathService, SHA256 | DownloadTaskOrchestratorService |
| WinDownloader.Wim | ManagedWimLib | EsdToIsoConversionService（主应用）, CliConversionService（POC） |
| WinDownloader.Iso | bundled Oscdimg | EsdToIsoConversionService（主应用）, CliConversionService（POC） |
| CacheService | Microsoft.Data.Sqlite | DownloadTaskOrchestratorService, Host |
| DownloadTaskOrchestratorService | ICacheService, IEsdDownloadPipeline, IDownloadTaskPathService, IAppSettings, IEsdToIsoOrchestratorService | SelectionViewModel, DownloadPageViewModel, DownloadTaskItemViewModel |
| EsdToIsoOrchestratorService | IEsdToIsoConversionService, IDownloadTaskPathService | DownloadTaskOrchestratorService, DownloadPageViewModel, DownloadTaskItemViewModel, Host |
| AppSettingsService | JSON, UserDataPaths | DownloadService, path service, settings UI |

## POC 边界

`src/POC` 通过 `CliConversionService` 验证共享 WIM/ISO 库、压缩参数和转换输出，不包含主应用的 UI 或任务持久化。命令和诊断项见 [POC 模块](MODULE_POC.md)。
