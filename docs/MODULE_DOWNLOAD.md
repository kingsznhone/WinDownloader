# WindowsImageDownloader — ESD 下载与 ISO 转换调度模块

## 概述

下载模块负责把目录条目变成可持久化、可暂停/恢复、可校验的 ESD 下载任务，并在下载完成后调度可选的 ESD 到 ISO 转换。ESD 下载任务持久化到 SQLite；ISO 转换任务只在内存中运行，通过独立快照通知 UI。

核心职责拆分：

| 职责 | 类型 |
|------|------|
| 底层 HTTP 下载 | `IDownloadService` / `DownloadService` |
| ESD 路径解析 | `IDownloadTaskPathService` / `DownloadTaskPathService` |
| 下载 + SHA-256 校验 | `IEsdDownloadPipeline` / `EsdDownloadPipeline` |
| ESD 到 ISO 转换 | `WindowsImageDownloader.Iso` / `IEsdToIsoConversionService` / `EsdToIsoConversionService` |
| WIM/ESD 原语 | `WindowsImageDownloader.Wim` / `IWimProcessingService` / `WimProcessingService` |
| ISO 创建后端 | `WindowsImageDownloader.Iso` / `IIsoCreationService` / `OscdimgIsoCreationService` |
| SQLite 任务缓存 | `ICacheService` / `CacheService` |
| 下载和转换编排、UI 事件 | `ITaskOrchestratorService` / `TaskOrchestratorService` |

## 文件清单

| 文件 | 说明 |
|------|------|
| `Interfaces/IDownloadService.cs` | 下载服务接口和 `DownloadProgress` record |
| `Services/DownloadService.cs` | Downloader NuGet 包装 |
| `Interfaces/IDownloadTaskPathService.cs` | ESD、ISO 和 staging 路径解析接口 |
| `Services/DownloadTaskPathService.cs` | 下载目录、ESD、ISO、临时路径实现 |
| `Interfaces/IEsdDownloadPipeline.cs` | ESD 下载和校验接口 |
| `Services/EsdDownloadPipeline.cs` | 调用下载服务并执行 SHA-256 校验 |
| `../WindowsImageDownloader.Iso/IEsdToIsoConversionService.cs` | ESD 到 ISO 转换服务接口 |
| `../WindowsImageDownloader.Iso/Services/EsdToIsoConversionService.cs` | 转换流水线实现 |
| `../WindowsImageDownloader.Wim/IWimProcessingService.cs` | WIM/ESD 操作接口 |
| `../WindowsImageDownloader.Wim/Services/WimProcessingService.cs` | ManagedWimLib 包装实现 |
| `../WindowsImageDownloader.Iso/IIsoCreationService.cs` | ISO 创建接口 |
| `../WindowsImageDownloader.Iso/Services/OscdimgIsoCreationService.cs` | oscdimg 后端实现 |
| `Interfaces/ICacheService.cs` | 缓存服务接口 |
| `Services/CacheService.cs` | SQLite 缓存实现 |
| `Interfaces/ITaskOrchestratorService.cs` | 任务编排接口 |
| `Services/TaskOrchestratorService.cs` | 任务编排实现 |
| `Services/DownloadTaskSnapshot.cs` | 后台线程传给 UI 的任务快照 |
| `Services/TaskOperationResult.cs` | 操作结果 record |

## DownloadService

```csharp
Task DownloadAsync(
    string url,
    string destinationPath,
    IProgress<DownloadProgress> progress,
    CancellationToken cancellationToken = default);
```

实现要点：

- 使用 Downloader NuGet。
- `DownloadService` 可作为 Singleton 注册；每次 `DownloadAsync` 都会创建独立的 `Downloader.DownloadService` 实例。
- 下载配置来自 `IAppSettings`：`DownloadChunkCount`、`DownloadParallelCount`。
- `EnableAutoResumeDownload = true`，暂停后再次下载可使用同一路径续传。
- 通过 `CancellationToken.Register` 调用底层 `CancelAsync()`。

## DownloadTaskPathService

路径规则：

```text
目录: {DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}
ESD:  {目录}\{FileName without extension}.esd
ISO:  {目录}\{FileName without extension}.iso
ISO staging: {目录}\.staging
临时: {ESD}.download
```

所有下载、转换、打开目录、删除文件逻辑都应通过 `IDownloadTaskPathService` 获取路径，避免把路径拼接散落在模型或 UI 中。

## EsdDownloadPipeline

```text
DownloadAsync(task)
  → ResolveEsdPath(task)
  → Directory.CreateDirectory
  → IDownloadService.DownloadAsync(task.DownloadUrl, esdPath)

VerifyAsync(task)
  → 读取 ESD 文件
  → 计算 SHA-256 十六进制
  → 与 task.Sha256 比较
  → 不一致时抛出 InvalidDataException
```

`TaskOrchestratorService` 负责状态变化和持久化；pipeline 只负责实际下载与校验。

## CacheService

### Schema

```sql
CREATE TABLE IF NOT EXISTS DownloadTasks (
    Sha256              TEXT    NOT NULL PRIMARY KEY,
    RawFileGroup        TEXT    NOT NULL,
    State               INTEGER NOT NULL DEFAULT 0,
    DownloadedBytes     INTEGER NOT NULL DEFAULT 0,
    ErrorMessage        TEXT,
    CreatedAt           TEXT    NOT NULL,
    UpdatedAt           TEXT    NOT NULL
);
```

实现要点：

- 数据库路径：`%LocalAppData%\WindowsImageDownloader\cache.db`。
- `RequiredColumns` 用于检测 schema 是否兼容。
- schema 不兼容或数据库损坏时自动删除重建，最多重试一次。
- `RawFileGroup` 以 JSON 存储原始目录文件组，包含代表 `RawFile` 和完整 editions 列表。
- `Progress`、`SpeedBytesPerSecond`、`StatusText` 是运行时 UI 状态，不持久化。
- 开发期 schema 重构不做旧平铺列迁移；旧缓存会按不兼容 schema 处理并重建。

## TaskOrchestratorService

### 状态流

```text
Queued
  → Downloading
  → Verifying
  → Completed

任意执行阶段异常 → Failed
暂停下载 → Queued
取消/删除 → 从缓存和 UI 集合移除
```

### 主要 API

```csharp
Task<TaskOperationResult> EnqueueAsync(DownloadTask task, CancellationToken ct = default);
Task RequeueAsync(string sha256, CancellationToken ct = default);
Task<TaskOperationResult> PauseAsync(string sha256, CancellationToken ct = default);
Task<TaskOperationResult> ResumeAsync(string sha256, CancellationToken ct = default);
Task<TaskOperationResult> CancelAsync(string sha256, CancellationToken ct = default);
Task<TaskOperationResult> ConvertToIsoAsync(string sha256, CancellationToken ct = default);
Task<TaskOperationResult> DeleteAsync(string sha256, CancellationToken ct = default);
IReadOnlyList<DownloadTask> Tasks { get; }
int ActiveTaskCount { get; }
```

### 行为

| 操作 | 行为 |
|------|------|
| `EnqueueAsync` | 去重、写入缓存、插入 UI 集合、调度下载 |
| `PauseAsync` | 取消当前下载流，状态回到 `Queued` |
| `ResumeAsync` | 重新调度 `Queued` 任务 |
| `CancelAsync` | 取消任务、删除 `.download` 临时文件、删除缓存记录 |
| `ConvertToIsoAsync` | 仅允许已完成 ESD 下载的任务排队转换；已有 ISO 时返回成功，由 UI 打开目录 |
| `DeleteAsync` | 仅允许删除 `Completed` 且没有 ISO 转换中的任务，同时删除已校验 ESD 文件 |
| `RequeueAsync` | 重置进度和错误状态，从头重新下载 |

### 启动恢复

`StartAsync` 从 SQLite 加载任务：

- `Downloading` / `Verifying` 会重置为 `Queued`。
- `Completed` 但本地 ESD 文件缺失，会重置为 `Queued` 并提示重新下载。

### ISO 转换调度

`ConvertToIsoAsync` 会检查：

1. 任务存在。
2. 下载任务状态为 `Completed`。
3. 本地 ESD 文件存在。
4. 当前任务没有已经排队或运行的 ISO conversion worker。

如果最终 ISO 文件已存在，方法返回成功但不会创建 worker；`DownloadTaskItemViewModel` 会直接打开 ISO 所在目录。否则，orchestrator 创建一个后台 worker，并先发布 `NotStarted` 快照。

ISO 转换 worker 固定单并发：

```text
WaitForIsoConversionSlotAsync
  → EsdToIsoConversionService.ConvertAsync
  → ProgressChanged(EsdToIsoTaskSnapshot)
  → PublishIsoSnapshot
  → DownloadTaskSnapshot.IsoConversionSnapshot
  → DownloadTaskItemViewModel
```

转换请求使用：

```text
SourceEsdPath = ResolveEsdPath(task)
StagingDirectory = ResolveIsoStagingDirectory(task)  # {任务目录}\.staging
IsoPath = {任务目录}\{FileNameWithoutExtension}.iso
KeepIntermediateFiles = false
InstallCompression = LZMS
RecompressInstallImage = false  # 默认复用官方 solid LZMS 资源写入 install.wim
```

`EsdToIsoConversionService` 在开始时会清理旧 `.staging`，完成、失败或取消后在 `finally` 中再次尽力删除 `.staging`。删除失败会被吞掉；下次转换会重新尝试清理。

### ActiveTaskCount

`ActiveTaskCount` 统计当前正在执行的下载 worker 和 ISO conversion worker。下载页 InfoBadge 读取这个值，而不是简单统计未完成任务数。这样排队但未占用下载槽的任务不会被当作 active，正在转换 ISO 的任务会被计入 active。

### 服务层组织

`TaskOrchestratorService` 当前同时管理下载 worker、ISO conversion worker、快照转发、SQLite 状态同步和 active count，是服务层中最重的文件。短期保留集中实现，因为下载页任务项需要一个统一生命周期入口；如果后续要给 ISO 转换增加取消按钮、恢复、重试、持久化或多队列策略，应拆出独立转换编排服务。

### 应用退出时的 ISO 转换处理

窗口关闭时，Host 会调用 `TaskOrchestratorService.StopAsync`。orchestrator 取消 `_shutdownCts`，等待下载和 ISO worker 结束。ISO worker 捕获关闭触发的取消后发布 `Canceled` 快照并释放 conversion slot。

底层转换的清理策略：

- `EsdToIsoConversionService` 在 `finally` 中删除 `.staging`。
- `OscdimgIsoCreationService` 在取消时 kill `oscdimg.exe` 进程树。
- 转换任务不写入 SQLite；重启后不会自动恢复。
- 如果文件被占用或进程退出不及时，可能留下 `.staging` 或半成品 ISO。

## UI 事件

| 事件 | 说明 |
|------|------|
| `TaskAdded` | UI 线程插入新的任务项 |
| `TaskRemoved` | UI 线程移除任务项 |
| `TaskChanged` | 可能来自后台线程，使用 `DownloadTaskSnapshot` 携带状态 |
| `ActiveTaskCountChanged` | 下载或 ISO worker active 计数变化时触发 |

`DownloadTaskItemViewModel` 会合并高频快照，并通过 `DispatcherQueue.TryEnqueue` 在 UI 线程更新绑定属性。`DownloadTaskSnapshot.IsoConversionSnapshot` 用于在同一个 task item 上显示 ISO 转换主进度和子进度。

## 注意事项

- `MaxConcurrentDownloads` 在下载任务准备启动时读取；调低不会中断已运行下载，但会限制后续任务启动。
- `DownloadService` 内部底层 Downloader 实例不可跨下载复用；当前包装器每次调用都会新建实例。
- ISO 转换固定单并发，且 CPU/IO 密集；不要与下载并发设置混用。
- 下载任务 schema 不保存 ISO 转换状态；不要直接给 SQLite 增加转换字段，除非先设计恢复语义。
