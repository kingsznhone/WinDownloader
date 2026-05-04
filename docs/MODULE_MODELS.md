# WindowsImageDownloader — 数据模型模块

## 概述

主应用的数据模型只描述产品目录、ESD 下载任务、任务状态和 UI 选项。WIM/ISO 相关模型已迁移到 POC 项目。

## 文件清单

| 文件 | 说明 |
|------|------|
| `Models/CatalogOption.cs` | 筛选器选项 |
| `Models/DownloadTask.cs` | ESD 下载任务 |
| `Models/RawFile.cs` | products.xml 中的单个文件条目 |
| `Models/RawFileGroup.cs` | 按下载 URL 聚合后的文件组 |
| `Models/TagType.cs` | UI 标签颜色类型 |
| `Models/TaskState.cs` | 下载任务生命周期状态 |

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

主应用没有 `Converting` 状态。

## 路径模型

路径不属于 `DownloadTask` 职责，由 `IDownloadTaskPathService` 统一解析：

```text
{DownloadDirectory}\WindowsImage\{LanguageCode}\{Architecture}\{FileNameWithoutExtension}.esd
```

这样可以避免模型持有环境依赖，也让 UI、删除逻辑、下载管道使用同一套路径规则。

## TagType

`TagType` 用于 `TagControl` 的颜色变体：`Default`、`Primary`、`Success`、`Warning`、`Danger`、`Info`。
