# WindowsImageDownloader — ESD 下载模块

## 概述

下载模块负责把目录条目变成可持久化、可暂停/恢复、可校验的 ESD 下载任务。它覆盖底层下载、SHA-256 校验、SQLite 任务缓存、下载任务编排和下载状态 UI 事件。

ESD 下载完成后的 ISO 转换由独立模块负责；下载模块只保留必要的边界协作，例如删除已完成任务前检查该任务是否仍在转换中。转换调度、`.staging`、WIM/ISO 服务和转换快照见 [MODULE_CONVERSION.md](MODULE_CONVERSION.md)。

核心职责拆分：

| 职责 | 类型 |
|------|------|
| 底层 HTTP 下载 | `IDownloadService` / `DownloadService` |
| ESD 路径解析 | `IDownloadTaskPathService` / `DownloadTaskPathService` |
| 下载 + SHA-256 校验 | `IEsdDownloadPipeline` / `EsdDownloadPipeline` |
| ESD 下载任务编排、UI 事件 | `IDownloadTaskOrchestratorService` / `DownloadTaskOrchestratorService` |
| SQLite 任务缓存 | `ICacheService` / `CacheService` |

## 文件清单

| 文件 | 说明 |
|------|------|
| `Interfaces/IDownloadService.cs` | 下载服务接口和 `DownloadProgress` record |
| `Services/DownloadService.cs` | Downloader NuGet 包装 |
| `Interfaces/IDownloadTaskPathService.cs` | ESD、ISO 和 staging 路径解析接口 |
| `Services/DownloadTaskPathService.cs` | 下载目录、ESD、ISO、临时路径实现 |
| `Interfaces/IEsdDownloadPipeline.cs` | ESD 下载和校验接口 |
| `Services/EsdDownloadPipeline.cs` | 调用下载服务并执行 SHA-256 校验 |
| `Interfaces/IDownloadTaskOrchestratorService.cs` | ESD 下载任务编排接口 |
| `Services/DownloadTaskOrchestratorService.cs` | ESD 下载任务编排实现 |
| `Interfaces/ICacheService.cs` | 缓存服务接口 |
| `Services/CacheService.cs` | SQLite 缓存实现 |
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
ESD:  {目录}\{FileNameWithoutExtension}.esd
临时: {ESD}.download
```

下载、打开目录、删除文件逻辑都应通过 `IDownloadTaskPathService` 获取路径，避免把路径拼接散落在模型或 UI 中。ISO 和 `.staging` 路径同样由该服务解析，但属于转换模块的消费面，见 [MODULE_CONVERSION.md](MODULE_CONVERSION.md)。

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

`DownloadTaskOrchestratorService` 负责状态变化和持久化；pipeline 只负责实际下载与校验。

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

## DownloadTaskOrchestratorService

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
| `DeleteAsync` | 仅允许删除 `Completed` 且没有 ISO 转换中的任务，同时删除已校验 ESD 文件 |
| `RequeueAsync` | 重置进度和错误状态，从头重新下载 |

### 启动恢复

`StartAsync` 从 SQLite 加载任务：

- `Downloading` / `Verifying` 会重置为 `Queued`。
- `Completed` 但本地 ESD 文件缺失，会重置为 `Queued` 并提示重新下载。

## 与转换模块的边界

下载完成后不会自动转换 ISO；用户必须在下载任务项中手动触发转换。转换请求、固定单并发、`.staging` 清理和转换进度见 [MODULE_CONVERSION.md](MODULE_CONVERSION.md)。

下载模块与转换模块只有两个直接协作点：

- `DeleteAsync` 删除已完成 ESD 前调用 `IEsdToIsoOrchestratorService.IsConversionQueuedOrRunning()`，避免删除正在转换的源文件。
- 下载页徽标由 `DownloadPageViewModel` 聚合下载 active count 和转换 active count，下载模块本身只暴露下载 worker 计数。

## UI 事件

| 事件 | 说明 |
|------|------|
| `DownloadTaskOrchestratorService.TaskAdded` | UI 线程插入新的任务项 |
| `DownloadTaskOrchestratorService.TaskRemoved` | UI 线程移除任务项 |
| `DownloadTaskOrchestratorService.TaskChanged` | 可能来自后台线程，使用 `DownloadTaskSnapshot` 携带下载状态 |
| `DownloadTaskOrchestratorService.ActiveTaskCountChanged` | 下载 worker active 计数变化时触发 |

`DownloadTaskItemViewModel` 会合并下载快照，并通过 `DispatcherQueue.TryEnqueue` 在 UI 线程更新绑定属性。ISO 转换快照由独立转换事件传递，见 [MODULE_CONVERSION.md](MODULE_CONVERSION.md)。

## 注意事项

- `MaxConcurrentDownloads` 在下载任务准备启动时读取；调低不会中断已运行下载，但会限制后续任务启动。
- `DownloadService` 内部底层 Downloader 实例不可跨下载复用；当前包装器每次调用都会新建实例。
- ISO 转换固定单并发，且 CPU/IO 密集；不要把转换并发或转换状态塞回下载模块。
- 下载任务 schema 不保存 ISO 转换状态；不要直接给 SQLite 增加转换字段，除非先设计恢复语义。
