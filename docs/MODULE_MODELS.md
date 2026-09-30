# WindowsImageDownloader — 数据模型模块

## 概述

主应用的数据模型描述产品目录、ESD 下载任务、任务状态、UI 选项，以及 ESD 到 ISO 转换所需的 WIM/ISO 请求、结果和进度快照。下载任务仍然只持久化 ESD 下载状态；ISO 转换状态不写入 SQLite。

## 文件清单

| 文件 | 说明 |
|------|------|
| `Models/CatalogOption.cs` | 筛选器选项 |
| `Models/ConversionSession.cs` | 转换会话、进度节流和整体进度映射 |
| `Models/DownloadTask.cs` | ESD 下载任务 |
| `Models/EsdToIsoRequest.cs` | ESD 到 ISO 转换请求 |
| `Models/EsdToIsoResult.cs` | ESD 到 ISO 转换结果 |
| `Models/EsdToIsoTaskSnapshot.cs` | ISO 转换状态、阶段和进度快照 |
| `../WinDownloader.Iso/Models/IsoCreationRequest.cs` / `IsoCreationResult.cs` | 共享 ISO 创建后端请求和结果 |
| `../WinDownloader.Iso/Models/IsoOperationProgress.cs` | 共享 oscdimg 进度 |
| `Models/RawFile.cs` | products.xml 中的单个文件条目 |
| `Models/RawFileGroup.cs` | 按下载 URL 聚合后的文件组 |
| `Models/TagType.cs` | UI 标签颜色类型 |
| `Models/TaskState.cs` | 下载任务生命周期状态 |
| `../WinDownloader.Wim/Models/Wim*.cs` | 共享 WIM/ESD 映像信息、导出/提取请求和进度 |

文件路径相对 `src/WinDownloader/`。

## RawFile

`RawFile` 是产品目录解析出的原始条目，关键字段：

| 属性 | 说明 |
|------|------|
| `FilePath` | ESD 下载 URL |
| `FileName` | 原始文件名 |
| `Sha256` | 服务器声明的 SHA-256 十六进制哈希 |
| `Size` | 文件大小 |
| `LanguageCode` / `Language` | 语言代码和名称 |
| `Architecture` | 架构 |
| `EditionLoc` / `Edition` | 版本组和具体版本 |
| `IsRetailOnly` | 是否仅零售版 |

## RawFileGroup

多个 `RawFile` 可能指向同一个 ESD 文件，但代表不同 edition。`SelectionViewModel` 按 `FilePath` 分组，生成：

| 属性 | 说明 |
|------|------|
| `File` | 代表性条目 |
| `Editions` | 同一 ESD 内包含的全部 edition 名称 |

## DownloadTask

`DownloadTask` 不直接通知绑定；UI 更新通过 `DownloadTaskSnapshot` 传递给 ViewModel。

### 身份和目录字段

| 属性 | 说明 |
|------|------|
| `FileGroup` | 原始 `RawFileGroup`，包含代表 `RawFile` 和完整 editions 列表 |
| `Sha256` | 从 `FileGroup.File.Sha256` 转发，作为缓存主键和任务标识 |
| `LanguageCode` / `Architecture` / `FileName` | 从 `FileGroup.File` 转发，供路径解析和下载流程使用 |
| `DownloadUrl` / `TotalBytes` | 从 `FileGroup.File.FilePath` / `Size` 转发，供下载和进度计算使用 |

### 运行时字段

| 属性 | 说明 | 是否持久化 |
|------|------|:----------:|
| `State` | 当前生命周期状态 | 是 |
| `DownloadedBytes` | 已下载字节数 | 是 |
| `Progress` | UI 进度 `[0,1]` | 否 |
| `SpeedBytesPerSecond` | 当前速度 | 否 |
| `StatusText` | UI 状态文本 | 否 |
| `ErrorMessage` | 失败原因 | 是 |
| `CreatedAt` / `UpdatedAt` | 时间戳 | 是 |

从目录文件组创建任务使用 `DownloadTask.FromRawFileGroup(group)`。

## TaskState

状态为 `Queued`、`Downloading`、`Verifying`、`Completed`、`Failed`；迁移规则见 [下载模块](MODULE_DOWNLOAD.md#状态流)。

下载任务状态仍不包含 ISO 转换态。转换生命周期由 `EsdToIsoTaskSnapshot.State` 表示，并通过 `IsoConversionTaskSnapshot` 独立通知 UI。

## ISO 转换模型

### EsdToIsoRequest

| 属性 | 说明 |
|------|------|
| `SourceEsdPath` | 本地 ESD 文件路径 |
| `StagingDirectory` | ISO 转换 staging 目录；主应用使用 `{任务目录}\.staging` |
| `VolumeLabel` | ISO 卷标，默认 `ESD_ISO` |
| `KeepIntermediateFiles` | 是否保留中间文件；主应用默认为 `false` |
| `InstallCompression` | `install.wim` 压缩算法，默认 `LZMS` |
| `RecompressInstallImage` | 是否强制重压安装映像；默认 `false`，即复用官方 solid LZMS 资源写入 `install.wim` |

### EsdToIsoTaskSnapshot

| 字段 | 说明 |
|------|------|
| `TaskId` / `SourceEsdPath` | 转换任务标识和输入 |
| `State` | `NotStarted` / `Running` / `Completed` / `Failed` / `Canceled` |
| `Stage` | `Preparing`、`InspectingSource`、`ApplyingSetupMedia`、`BuildingBootWim`、`BuildingInstallImage`、`CreatingIso` 等 |
| `Progress` | 整体归一化进度，范围 `[0, 1]` |
| `CurrentFile` | 当前处理路径 |
| `ErrorMessage` | 失败或取消原因 |
| `IsoPath` | ISO 输出路径 |
| `WimProgress` | ManagedWimLib 子进度 |
| `IsoProgress` | oscdimg 子进度 |

`ConversionSession` 维护阶段高水位，保证 `Running` 快照的整体进度不回退。

### WIM / ISO 模型

共享后端的请求、结果和进度字段分别见 [WIM 模块](MODULE_WIM.md#模型) 和 [ISO 模块](MODULE_ISO.md#模型)。

## 路径模型

路径由 `IDownloadTaskPathService` 统一解析，不由模型拼接；具体规则见 [下载路径](MODULE_DOWNLOAD.md#downloadtaskpathservice) 和 [转换路径](MODULE_CONVERSION.md#路径规则)。

## TagType

`TagType` 用于 `TagControl` 的颜色变体：`Default`、`Primary`、`Success`、`Warning`、`Danger`、`Info`。
