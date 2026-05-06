# WindowsImageDownloader — Copilot 编码上下文

## 当前定位

主 WinUI 应用是 ESD 下载 + ISO 转换工具：产品目录获取、ESD 下载、SHA-256 校验、SQLite 任务持久化、下载任务 UI 管理，以及下载完成后的可选 ESD 到 ISO 转换。

主项目通过共享库集成 ISO 转换：`WindowsImageDownloader.Wim` 封装 `ManagedWimLib`，`WindowsImageDownloader.Iso` 封装 ESD→ISO 流水线和随输出复制的 `Oscdimg\oscdimg.exe`。默认安装映像输出为复用官方 solid LZMS 资源的 `sources\install.wim`。不要在没有重新设计的情况下加入 `OutputFormat` 设置、WIM/ISO 持久化任务字段或下载任务 `TaskState.Converting`；当前 ISO 转换状态通过独立快照传递给 UI，不写入 SQLite。

## 技术栈

- UI 框架: WinUI 3 (`Microsoft.UI.Xaml`)
- 运行时: .NET 10 + Windows App SDK 2.0
- MVVM: CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`)
- DI / 生命周期: `Microsoft.Extensions.Hosting` + `IHostedService`
- 下载引擎: Downloader NuGet，内部每个下载创建独立 Downloader 实例
- ISO 转换: `WindowsImageDownloader.Wim` + `WindowsImageDownloader.Iso` + bundled Oscdimg
- 数据库: Microsoft.Data.Sqlite
- 设置存储: JSON 文件
- 打包: 非 MSIX 解包部署 (`WindowsPackageType=None`)
- POC 验证: `src/POC` 保留同形控制台宿主，用于验证转换流水线和进度映射

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
│   ├── IUpdateCatalogService.cs
│   └── ...
├── Models/                             # ESD 下载、ISO 转换和 UI 模型
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

src/WindowsImageDownloader.Wim/             # ManagedWimLib 封装库
├── IWimProcessingService.cs
├── Models/Wim*.cs
└── Services/WimProcessingService.cs

src/WindowsImageDownloader.Iso/             # ESD→ISO 流水线和 oscdimg 后端库
├── IEsdToIsoConversionService.cs
├── IIsoCreationService.cs
├── Models/ConversionSession.cs / EsdToIso*.cs / Iso*.cs
└── Services/EsdToIsoConversionService.cs / OscdimgIsoCreationService.cs

src/POC/                                # 控制台验证/对照宿主
├── Program.cs
├── README.md
└── Oscdimg/
```

## DI 注册

| 生命周期 | 实例 |
|----------|------|
| `AddSingleton` | `IAppSettings`, `IUpdateCatalogService`, `ICacheService`, `IDownloadService`, `IDownloadTaskPathService`, `IEsdDownloadPipeline`, `IWimProcessingService`, `IIsoCreationService`, `IEsdToIsoConversionService`, `ITaskOrchestratorService`, `SelectionViewModel`, `SettingsViewModel`, `DownloadPageViewModel` |
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

## ISO 转换流程

```text
DownloadTaskItemViewModel.ConvertToIsoAsync
  → TaskOrchestratorService.ConvertToIsoAsync
  → 单并发 ISO conversion worker
  → EsdToIsoConversionService.ConvertAsync
  → WindowsImageDownloader.Wim.WimProcessingService.GetImagesAsync / ExtractImageAsync / ExportImagesAsync
  → image 4..n 默认复用官方 solid LZMS 资源写入 sources\install.wim
  → WindowsImageDownloader.Iso.OscdimgIsoCreationService.CreateIsoAsync
  → EsdToIsoTaskSnapshot 合并到 DownloadTaskSnapshot
  → DownloadTaskItemViewModel 显示 main/sub progress
```

已有最终 ISO 时，转换按钮直接打开 ISO 所在目录。转换中退出应用时，Host 会取消 worker，转换服务在 `finally` 中尽力删除 `.staging`，oscdimg 进程会被 kill；转换任务本身不持久化，重启后不会自动恢复。

## SQLite Schema 修改

- Schema 直接修改 `CacheService.CreateTableSql`。
- 同步更新 `RequiredColumns`、`MapToTask()`、`BindAllParameters()`、`UpdateTaskAsync()`。
- 不兼容 schema 或数据库损坏时会自动删除重建，任务历史会丢失。

## 线程安全

- UI 集合和绑定属性更新必须 marshal 到 UI 线程。
- `TaskOrchestratorService._taskMap` 使用 `ConcurrentDictionary`。
- `TaskOrchestratorService._tasks` 是 `ObservableCollection`，只在 UI 线程添加/删除，后台线程只读。
- `TaskChanged` 可能从后台线程触发；`DownloadTaskItemViewModel` 使用 `DispatcherQueue.TryEnqueue` 合并并应用下载和 ISO 快照。
- 下载并发由 `TaskOrchestratorService` 在任务启动前读取 `MaxConcurrentDownloads` 控制。
- ISO 转换并发固定为 1；下载页徽标读取 `ActiveTaskCount`，合并下载和 ISO 转换 worker。
- `TaskOrchestratorService.cs` 当前同时承担下载编排、ISO 转换编排、快照转发和 active count，是服务层中最重的文件；短期保持集中，未来若加入 ISO 取消/恢复/重试/持久化，应拆出独立转换编排服务。

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
| ISO 输出 | `{DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\{FileNameWithoutExtension}.iso` | ESD 转换成品 |
| ISO staging | `{DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\.staging` | 转换中间文件，默认完成后删除 |
