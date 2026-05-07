# WindowsImageDownloader — ESD 到 ISO 转换调度模块

## 概述

转换模块负责用户手动触发的 ESD 到 ISO 转换。它覆盖 ISO 转换 worker 调度、固定单并发、转换进度快照、`.staging` 清理，以及主应用内的 ESD→ISO 流水线编排。

ESD 下载、SHA-256 校验、SQLite 下载任务缓存和下载状态 UI 事件见 [MODULE_DOWNLOAD.md](MODULE_DOWNLOAD.md)。转换任务不写入 SQLite，不扩展下载任务 `TaskState`，重启后不会自动恢复。

核心职责拆分：

| 职责 | 类型 |
|------|------|
| ISO 转换任务编排、UI 事件 | `IEsdToIsoOrchestratorService` / `EsdToIsoOrchestratorService` |
| ESD 到 ISO 转换流水线编排 | `IEsdToIsoConversionService` / `EsdToIsoConversionService`（主应用内） |
| ESD、ISO、`.staging` 路径解析 | `IDownloadTaskPathService` / `DownloadTaskPathService` |
| WIM/ESD 原语 | `WinDownloader.Wim` / `IWimProcessingService` / `WimProcessingService`（见 [MODULE_WIM.md](MODULE_WIM.md)） |
| ISO 创建后端 | `WinDownloader.Iso` / `IIsoCreationService` / `OscdimgIsoCreationService`（见 [MODULE_ISO.md](MODULE_ISO.md)） |
| 下载任务删除保护 | `IDownloadTaskOrchestratorService` 查询转换编排状态 |

## 文件清单

| 文件 | 说明 |
|------|------|
| `Interfaces/IEsdToIsoOrchestratorService.cs` | ISO 转换任务编排接口 |
| `Services/EsdToIsoOrchestratorService.cs` | ISO 转换 worker 编排实现 |
| `Services/IsoConversionTaskSnapshot.cs` | ISO 转换快照事件包装 record |
| `Interfaces/IEsdToIsoConversionService.cs` | ESD 到 ISO 转换服务接口 |
| `Services/EsdToIsoConversionService.cs` | ESD→ISO 流水线实现（主应用内） |
| `Models/ConversionSession.cs` | 转换会话、进度节流和整体进度映射 |
| `Models/EsdToIsoRequest.cs` | 转换请求 |
| `Models/EsdToIsoResult.cs` | 转换结果 |
| `Models/EsdToIsoTaskSnapshot.cs` | 转换状态、阶段和进度快照 |
| `Interfaces/IDownloadTaskPathService.cs` | ESD、ISO 和 `.staging` 路径解析接口 |
| `Services/DownloadTaskPathService.cs` | 路径解析实现 |
| `WinDownloader.Wim`（共享库） | WIM/ESD 原语——见 [MODULE_WIM.md](MODULE_WIM.md) |
| `WinDownloader.Iso`（共享库） | ISO 创建后端——见 [MODULE_ISO.md](MODULE_ISO.md) |

## 与下载模块的边界

- 下载完成后不会自动转换 ISO；用户必须在下载任务项中手动触发。
- 转换编排接收已完成的 `DownloadTask`，但不修改下载任务 state，也不写入 SQLite。
- 删除已完成 ESD 时，下载编排服务会调用 `IsConversionQueuedOrRunning()` 做删除保护。
- 下载页 InfoBadge 在 `DownloadPageViewModel` 中聚合下载 active count 和转换 active count；两个编排服务分别维护各自计数。

## EsdToIsoOrchestratorService

### 主要 API

```csharp
Task<TaskOperationResult> ConvertToIsoAsync(DownloadTask task, CancellationToken ct = default);
bool IsConversionQueuedOrRunning(string sha256);
EsdToIsoTaskSnapshot? GetSnapshot(string sha256);
void ClearSnapshot(string sha256);
int ActiveTaskCount { get; }
event EventHandler<IsoConversionTaskSnapshot> ConversionChanged;
event EventHandler? ActiveTaskCountChanged;
```

### 调度检查

`ConvertToIsoAsync` 会检查：

1. 下载任务状态为 `Completed`。
2. 本地 ESD 文件存在。
3. 当前任务没有已经排队或运行的 ISO conversion worker。

如果最终 ISO 文件已存在，方法返回成功但不会创建 worker；`DownloadTaskItemViewModel` 会直接打开 ISO 所在目录。否则，ISO orchestrator 创建一个后台 worker，并先发布 `NotStarted` 快照。

### 固定单并发

ISO 转换 worker 固定单并发：

```text
WaitForConversionSlotAsync
  → EsdToIsoConversionService.ConvertAsync
  → ProgressChanged(EsdToIsoTaskSnapshot)
  → ConversionChanged(IsoConversionTaskSnapshot)
  → DownloadTaskItemViewModel
```

ISO 转换固定单并发，因为 ESD→ISO 同时消耗 CPU 和磁盘 I/O。这个限制由 `EsdToIsoOrchestratorService` 内部 conversion slot 控制，不读取应用设置，也不与 `MaxConcurrentDownloads` 混用。

### ActiveTaskCount

`EsdToIsoOrchestratorService.ActiveTaskCount` 只统计已获得固定 conversion slot 的 ISO worker。排队但尚未获得转换槽的任务不会计入 active。下载页 InfoBadge 在 `DownloadPageViewModel` 中把它与 `DownloadTaskOrchestratorService.ActiveTaskCount` 相加。

## EsdToIsoConversionService

转换请求使用：

```text
SourceEsdPath = ResolveEsdPath(task)
StagingDirectory = ResolveIsoStagingDirectory(task)  # {任务目录}\.staging
IsoPath = {任务目录}\{FileNameWithoutExtension}.iso
KeepIntermediateFiles = false
InstallCompression = LZMS
RecompressInstallImage = false  # 默认复用官方 solid LZMS 资源写入 install.wim
```

转换流水线：

```text
Preparing
  → 清理并创建 .staging
InspectingSource
  → WimProcessingService.GetImagesAsync
ApplyingSetupMedia
  → WimProcessingService.ExtractImageAsync image 1 到 staging
BuildingBootWim
  → WimProcessingService.ExportImagesAsync image 2+3 到 sources\boot.wim
BuildingInstallImage
  → WimProcessingService.ExportImagesAsync image 4..n 到 sources\install.wim
CreatingIso
  → OscdimgIsoCreationService.CreateIsoAsync
Completed / Failed / Canceled
```

`ConversionSession` 负责整体进度映射、节流和高水位保护。WIM 子阶段会被映射到外层阶段区间，oscdimg 百分比映射到 ISO 创建阶段，避免 UI 出现进度回跳。

## 路径规则

```text
ESD:         {任务目录}\{FileNameWithoutExtension}.esd
ISO:         {任务目录}\{FileNameWithoutExtension}.iso
ISO staging: {任务目录}\.staging
```

所有转换、打开 ISO 目录、清理 staging 的逻辑都应通过 `IDownloadTaskPathService` 获取路径。

## UI 事件

| 事件 | 说明 |
|------|------|
| `EsdToIsoOrchestratorService.ConversionChanged` | 可能来自后台线程，使用 `IsoConversionTaskSnapshot` 携带 ISO 转换状态 |
| `EsdToIsoOrchestratorService.ActiveTaskCountChanged` | ISO worker active 计数变化时触发，由下载页聚合 |

`DownloadTaskItemViewModel` 会合并转换快照，并通过 `DispatcherQueue.TryEnqueue` 在 UI 线程更新 ISO 主进度、子进度、按钮状态和错误提示。转换快照不再合并进 `DownloadTaskSnapshot`。

## 应用退出和清理

窗口关闭时，Host 会按注册顺序的反向停止 hosted services。`EsdToIsoOrchestratorService.StopAsync` 会先取消 ISO worker 并等待结束；随后 `DownloadTaskOrchestratorService.StopAsync` 取消下载 worker。ISO worker 捕获关闭触发的取消后发布 `Canceled` 快照并释放 conversion slot。

底层转换的清理策略：

- `EsdToIsoConversionService` 在开始时清理旧 `.staging`。
- `EsdToIsoConversionService` 在 `finally` 中删除 `.staging`。
- `OscdimgIsoCreationService` 在取消时 kill `oscdimg.exe` 进程树。
- 转换任务不写入 SQLite；重启后不会自动恢复。
- 如果文件被占用或进程退出不及时，可能留下 `.staging` 或半成品 ISO。

## 注意事项

- ISO 转换固定单并发，且 CPU/IO 密集；不要与下载并发设置混用，也不要新增 ISO 并发设置，除非重新评估资源占用策略。
- 不要把转换状态写回下载任务 state 或 SQLite；保持 `EsdToIsoTaskSnapshot` 独立通知 UI。
- 不要新增 `OutputFormat` 设置；默认安装映像输出为复用官方 solid LZMS 资源的 `sources\install.wim`。
- 若将来加入 ISO 取消/恢复/重试/持久化，应优先扩展 `EsdToIsoOrchestratorService`，并先设计转换恢复语义。
