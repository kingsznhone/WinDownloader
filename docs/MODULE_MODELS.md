# WindowsImageDownloader — 数据模型模块

## 概述

主应用的数据模型描述产品目录、ESD 下载任务、任务状态、UI 选项，以及 ESD 到 ISO 转换所需的 WIM/ISO 请求、结果和进度快照。下载任务仍然只持久化 ESD 下载状态；ISO 转换状态不写入 SQLite。

## 文件清单

| 文件 | 说明 |
|------|------|
| `Models/CatalogOption.cs` | 筛选器选项 |
| `Models/DownloadTask.cs` | ESD 下载任务 |
| `Models/EsdToIsoRequest.cs` | ESD 到 ISO 转换请求 |
| `Models/EsdToIsoResult.cs` | ESD 到 ISO 转换结果 |
| `Models/EsdToIsoTaskSnapshot.cs` | ISO 转换状态、阶段和进度快照 |
| `Models/IsoCreationRequest.cs` / `IsoCreationResult.cs` | ISO 创建后端请求和结果 |
| `Models/IsoOperationProgress.cs` | oscdimg 进度 |
| `Models/RawFile.cs` | products.xml 中的单个文件条目 |
| `Models/RawFileGroup.cs` | 按下载 URL 聚合后的文件组 |
| `Models/TagType.cs` | UI 标签颜色类型 |
| `Models/TaskState.cs` | 下载任务生命周期状态 |
| `Models/Wim*.cs` | WIM/ESD 映像信息、导出/提取请求和进度 |

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

`DownloadTask` 是主应用持久化和运行时共享的 ESD 下载任务模型。它是普通 sealed class，不继承 `ObservableObject`；UI 更新通过 `DownloadTaskSnapshot` 传递给 ViewModel。

### 身份和目录字段

| 属性 | 说明 |
|------|------|
| `Sha256` | 主键，等于目录条目的 SHA-256 |
| `LanguageCode` / `Language` | 语言信息 |
| `Architecture` | 架构 |
| `EditionLoc` / `Edition` | 版本组和代表版本 |
| `Editions` | 同一 ESD 内的所有版本 |
| `FileName` | 原始 ESD 文件名 |
| `DownloadUrl` | 下载 URL |
| `TotalBytes` | 文件大小 |
| `IsRetailOnly` | 是否仅零售版 |

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

### 创建方式

```csharp
var task = DownloadTask.FromRawFile(group.File, group.Editions);
```

## TaskState

```csharp
public enum TaskState
{
    Queued,
    Downloading,
    Verifying,
    Completed,
    Failed,
}
```

下载任务状态仍不包含 ISO 转换态。转换生命周期由 `EsdToIsoTaskSnapshot.State` 表示，并通过 `DownloadTaskSnapshot.IsoConversionSnapshot` 合并到 UI。

## ISO 转换模型

### EsdToIsoRequest

| 属性 | 说明 |
|------|------|
| `SourceEsdPath` | 本地 ESD 文件路径 |
| `StagingRoot` | ISO 转换 staging 目录；主应用使用 `{任务目录}\.staging` |
| `VolumeLabel` | ISO 卷标，默认 `ESD_ISO` |
| `KeepIntermediateFiles` | 是否保留中间文件；主应用默认为 `false` |
| `InstallCompression` | `install.esd` 压缩算法，默认 `LZMS` |

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

`ConversionSession` 在服务内部维护阶段高水位，保证 `Running` 快照的整体进度不回退。主应用的 staging 目录直接使用 `StagingRoot`；POC 为了保留独立验证目录，会在 `StagingRoot` 下再使用 `staging` 子目录。

### WIM / ISO 模型

| 模型 | 说明 |
|------|------|
| `WimImageInfo` | 从 ESD/WIM 读取的映像索引、名称、版本、语言、架构、boot 标记等 |
| `WimExtractRequest` | 提取单个映像到目录 |
| `WimExportRequest` / `WimImageExportItem` | 把多个源映像导出到目标 WIM/ESD |
| `WimOperationProgress` | ManagedWimLib 回调转换后的阶段、百分比、字节和当前项 |
| `IsoCreationRequest` | oscdimg 输入目录、输出 ISO 和卷标；可携带进度回调 |
| `IsoCreationResult` | oscdimg 命令、退出码、stdout/stderr、输出大小和 warnings |
| `IsoOperationProgress` | oscdimg 百分比 |

## 路径模型

路径不属于 `DownloadTask` 职责，由 `IDownloadTaskPathService` 统一解析：

```text
ESD: {DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\{FileNameWithoutExtension}.esd
ISO: {DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\{FileNameWithoutExtension}.iso
ISO staging: {DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\.staging
```

这样可以避免模型持有环境依赖，也让 UI、删除逻辑、下载管道使用同一套路径规则。

## TagType

`TagType` 用于 `TagControl` 的颜色变体：`Default`、`Primary`、`Success`、`Warning`、`Danger`、`Info`。
