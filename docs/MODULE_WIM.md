# WindowsImageDownloader — WIM 处理库模块

## 概述

`WinDownloader.Wim` 是独立的 ManagedWimLib 封装库，提供 WIM/ESD 文件的读取、镜像提取和镜像导出原语。主应用 `EsdToIsoConversionService` 和 POC `CliConversionService` 均依赖此库完成 ESD 到 ISO 转换流水线中的 WIM 阶段。

## 项目信息

| 属性 | 值 |
|------|------|
| 项目文件 | `src/WinDownloader.Wim/WinDownloader.Wim.csproj` |
| 目标框架 | `net10.0` |
| 主要依赖 | `ManagedWimLib` NuGet |

## 文件结构

```text
src/WinDownloader.Wim/
├── WinDownloader.Wim.csproj
├── Interfaces/
│   └── IWimProcessingService.cs     # 服务接口
├── Models/
│   ├── WimImageInfo.cs              # WIM 镜像信息（只读）
│   ├── WimExtractRequest.cs         # 单镜像提取请求
│   ├── WimExportRequest.cs          # 批量镜像导出请求
│   ├── WimImageExportItem.cs        # 单个导出镜像描述
│   └── WimOperationProgress.cs      # 进度快照和阶段枚举
└── Services/
    └── WimProcessingService.cs      # 实现
```

## IWimProcessingService

| 方法 | 输入与行为 |
|------|------------|
| `GetImagesAsync` | WIM/ESD 路径，返回所有映像元数据 |
| `ExtractImageAsync` | `WimExtractRequest`，提取单个映像到目录 |
| `ExportImagesAsync` | `WimExportRequest`，批量导出到新 WIM |

所有方法支持取消；提取和导出通过 `Action<WimOperationProgress>` 回调报告进度。

## WimProcessingService

- **只能作为 Singleton 注册**。内部持有 ManagedWimLib 全局初始化状态，并使用 `SemaphoreSlim(1, 1)` 保证同一时间只有一个 WIM 操作执行。
- 构造时自动在 `AppContext.BaseDirectory` 查找 `libwim-15.dll`：先查 `runtimes/win-x64/native/`（Debug / 非自包含），再查根目录（发布自包含）。找不到时把错误延迟到第一次操作，以便宿主应用正常启动。
- 操作获取信号量后在 `Task.Run` 中同步执行，取消通过 WimLib 回调传入。
- Host 释放服务时调用 `Dispose()` / `ManagedWim.TryGlobalCleanup()` 清理全局状态。

## 模型

### WimImageInfo

| 字段 | 说明 |
|------|------|
| `Index` | WIM 中的 1-based 镜像索引 |
| `Name` / `DisplayName` | 内部名称与显示名称 |
| `EditionId` / `InstallationType` | 系统版本标识与安装类型 |
| `Architecture` / `DefaultLanguage` | 架构与默认语言 |
| `TotalBytes` | 展开后占用字节数 |
| `IsBootable` | 是否为可启动镜像 |
| `Title` | 计算属性：DisplayName → Name → `Image {Index}` |
| `Subtitle` | 计算属性：拼接 EditionId / InstallationType / Architecture / DefaultLanguage |

### WimExtractRequest

```csharp
record WimExtractRequest(
    string SourceImagePath,
    int ImageIndex,           // 1-based
    string DestinationDirectory);
```

### WimExportRequest

```csharp
record WimExportRequest(
    string SourceImagePath,
    string DestinationImagePath,
    IReadOnlyList<WimImageExportItem> Images,
    CompressionType Compression = CompressionType.LZX,
    bool CheckIntegrity = true,
    bool Recompress = true,
    bool Solid = false,
    uint OutputChunkSize = 0,
    uint OutputPackChunkSize = 0);
```

共享库默认重压为 LZX；ESD→ISO 流水线生成 `install.wim` 时显式使用 LZMS、`Recompress=false`，复用官方 solid 资源。

### WimImageExportItem

```csharp
record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    ExportFlags ExportFlags = ExportFlags.None);
```

### WimOperationProgress / WimOperationStage

包含阶段、可空百分比（0-100）、已完成/总字节数及当前项。阶段为 `Opening`、`Extracting`、`Writing`、`Verifying`、`Metadata`、`Completed`、`Other`；并非每个阶段都有百分比。

## ESD 镜像分布约定

Windows ESD 文件的镜像索引约定（转换流水线消费方依赖此布局）：

| 索引 | 内容 |
|------|------|
| 1 | 安装媒体 setup 文件（展开到 staging 根目录） |
| 2 | Windows PE（写入 `boot.wim` 镜像 1） |
| 3 | Windows Setup PE（写入 `boot.wim` 镜像 2，可启动） |
| 4..n | Windows 安装版本（写入 `install.wim`） |

流水线实现见 [转换模块](MODULE_CONVERSION.md#esdtoisoconversionservice)。

