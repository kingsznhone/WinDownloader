# WindowsImageDownloader — Copilot 编码上下文

## 当前定位

主 WinUI 应用是 ESD-only 下载器：产品目录获取、ESD 下载、SHA-256 校验、SQLite 任务持久化、下载任务 UI 管理。WIM/ISO 后处理已迁移到 `src/POC`，只用于概念验证。

不要在主项目中重新引入 `OutputFormat`、WIM/ISO 转换状态、`ManagedWimLib`、`DiscUtils` 或转换管道，除非用户明确要求重新设计主应用后处理功能。

## 技术栈

- UI 框架: WinUI 3 (`Microsoft.UI.Xaml`)
- 运行时: .NET 10 + Windows App SDK 2.0
- MVVM: CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`)
- DI / 生命周期: `Microsoft.Extensions.Hosting` + `IHostedService`
- 下载引擎: Downloader NuGet，内部每个下载创建独立 Downloader 实例
- 数据库: Microsoft.Data.Sqlite
- 设置存储: JSON 文件
- 打包: 非 MSIX 解包部署 (`WindowsPackageType=None`)
- POC 后处理: `src/POC` 使用 ManagedWimLib / DiscUtils

## 项目结构

```text
src/WindowsImageDownloader/
├── App.xaml / App.xaml.cs              # 应用入口、Host/DI、生命周期
├── MainWindow.xaml / .cs               # NavigationView 导航壳
├── Converters/                         # 值转换器
├── Interfaces/                         # 服务接口
│   ├── IAppSettings.cs
│   ├── ICacheService.cs
│   ├── IDownloadService.cs
│   ├── IDownloadTaskPathService.cs
│   ├── IEsdDownloadPipeline.cs
│   ├── ITaskOrchestratorService.cs
│   └── IUpdateCatalogService.cs
├── Models/                             # ESD 下载和 UI 模型
│   ├── CatalogOption.cs
│   ├── DownloadTask.cs
│   ├── RawFile.cs / RawFileGroup.cs
│   ├── TagType.cs
│   └── TaskState.cs
├── Services/                           # 服务实现
│   ├── AppSettingsService.cs
│   ├── CacheService.cs
│   ├── DownloadService.cs
│   ├── DownloadTaskPathService.cs
│   ├── EsdDownloadPipeline.cs
│   ├── TaskOrchestratorService.cs
│   ├── UpdateCatalogService.cs
│   ├── DownloadTaskSnapshot.cs
│   └── TaskOperationResult.cs
├── ViewModels/
└── Views/

src/POC/
└── Wim/                                # WIM/ISO 后处理概念验证代码
```

## DI 注册

| 生命周期 | 实例 |
|----------|------|
| `AddSingleton` | `IAppSettings`, `IUpdateCatalogService`, `ICacheService`, `IDownloadService`, `IDownloadTaskPathService`, `IEsdDownloadPipeline`, `ITaskOrchestratorService`, `SelectionViewModel`, `SettingsViewModel`, `DownloadPageViewModel` |
| `AddHostedService` | `CacheService`, `TaskOrchestratorService` |

`AddHostedService` 通过 `sp.GetRequiredService<T>()` 复用已注册 Singleton。启动顺序是 CacheService 先建表，TaskOrchestratorService 后加载任务；停止顺序相反。

## 下载流程

```text
SelectionViewModel.EnqueueDownloadAsync
  → DownloadTask.FromRawFile(file, editions)
  → TaskOrchestratorService.EnqueueAsync
  → CacheService.AddTaskAsync
  → TaskOrchestratorService.ScheduleDownloadAsync
  → EsdDownloadPipeline.DownloadAsync
  → DownloadService.DownloadAsync
  → EsdDownloadPipeline.VerifyAsync
  → Completed / Failed
```

## SQLite Schema 修改

- Schema 直接修改 `CacheService.CreateTableSql`。
- 同步更新 `RequiredColumns`、`MapToTask()`、`BindAllParameters()`、`UpdateTaskAsync()`。
- 不兼容 schema 或数据库损坏时会自动删除重建，任务历史会丢失。

## 线程安全

- UI 集合和绑定属性更新必须 marshal 到 UI 线程。
- `TaskOrchestratorService._taskMap` 使用 `ConcurrentDictionary`。
- `TaskOrchestratorService._tasks` 是 `ObservableCollection`，只在 UI 线程添加/删除，后台线程只读。
- `TaskChanged` 可能从后台线程触发；`DownloadTaskItemViewModel` 使用 `DispatcherQueue.TryEnqueue` 合并并应用快照。
- 下载并发由 `TaskOrchestratorService` 在任务启动前读取 `MaxConcurrentDownloads` 控制。

## 设置项扩展流程

1. `Interfaces/IAppSettings.cs` 添加属性声明和 xml-doc。
2. `Services/AppSettingsService.cs` 添加 `Keys`、`Defaults`、属性实现。
3. ViewModel 添加包装属性和回调。
4. XAML 添加 UI 控件。
5. 更新 [docs/MODULE_SETTINGS.md](../docs/MODULE_SETTINGS.md)。

## 持久化路径

| 存储 | 路径 | 用途 |
|------|------|------|
| SQLite | `%LocalAppData%\WindowsImageDownloader\cache.db` | 下载任务持久化 |
| JSON | `%LocalAppData%\WindowsImageDownloader\settings.json` | 应用设置 |
| CAB 缓存 | `%LocalAppData%\WindowsImageDownloader\catalog_cache\` | 产品目录 CAB + XML |
