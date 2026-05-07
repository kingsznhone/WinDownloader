# WindowsImageDownloader — WIM 处理库模块

## 概述

`WinDownloader.Wim` 是独立的 ManagedWimLib 封装库，提供 WIM/ESD 文件的读取、镜像提取和镜像导出原语。主应用 `EsdToIsoConversionService` 和 POC `CliConversionService` 均依赖此库完成 ESD 到 ISO 转换流水线中的 WIM 阶段。

## 项目信息

| 属性 | 值 |
|------|------|
| 项目文件 | `src/WinDownloader.Wim/WinDownloader.Wim.csproj` |
| 命名空间 | `WinDownloader.Wim` |
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

```csharp
// 读取 WIM/ESD 中所有镜像的元数据
Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(
    string imagePath,
    CancellationToken cancellationToken = default);

// 提取单个镜像到目标目录
Task ExtractImageAsync(
    WimExtractRequest request,
    Action<WimOperationProgress>? progress = null,
    CancellationToken cancellationToken = default);

// 将一组镜像导出到新的 WIM 文件
Task ExportImagesAsync(
    WimExportRequest request,
    Action<WimOperationProgress>? progress = null,
    CancellationToken cancellationToken = default);
```

## WimProcessingService

- **只能作为 Singleton 注册**。内部持有 ManagedWimLib 全局初始化状态，并使用 `SemaphoreSlim(1, 1)` 保证同一时间只有一个 WIM 操作执行。
- 构造时自动在 `AppContext.BaseDirectory` 查找 `libwim-15.dll`：先查 `runtimes/win-x64/native/`（Debug / 非自包含），再查根目录（发布自包含）。找不到时把错误延迟到第一次操作，以便宿主应用正常启动。
- `Dispose()` 调用 `ManagedWim.TryGlobalCleanup()`，释放全局 WIM 库资源。

## 模型

### WimImageInfo

| 字段 | 说明 |
|------|------|
| `Index` | WIM 中的 1-based 镜像索引 |
| `Name` | 镜像名称 |
| `DisplayName` | 显示名称（优先于 Name） |
| `EditionId` | 版本标识（如 `Core`、`Professional`） |
| `InstallationType` | 安装类型（如 `Client`、`Server`） |
| `Architecture` | 处理器架构字符串 |
| `DefaultLanguage` | 默认语言代码 |
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

提取单个镜像到目标目录（index 1-based）。

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

将一组镜像从源 WIM 导出到新 WIM，支持指定压缩算法和输出块大小。

### WimImageExportItem

```csharp
record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    ExportFlags ExportFlags = ExportFlags.None);
```

### WimOperationProgress / WimOperationStage

```csharp
record WimOperationProgress(
    WimOperationStage Stage,
    double? Percent,
    ulong? CompletedBytes,
    ulong? TotalBytes,
    string? CurrentItem);

enum WimOperationStage
{
    Opening, Extracting, Writing, Verifying, Metadata, Completed, Other
}
```

## ESD 镜像分布约定

Windows ESD 文件的镜像索引约定（转换流水线消费方依赖此布局）：

| 索引 | 内容 |
|------|------|
| 1 | 安装媒体 setup 文件（展开到 staging 根目录） |
| 2 | Windows PE（写入 `boot.wim` 镜像 1） |
| 3 | Windows Setup PE（写入 `boot.wim` 镜像 2，可启动） |
| 4..n | Windows 安装版本（写入 `install.wim`） |

## DI 注册

```text
IWimProcessingService    Singleton    WimProcessingService
```

`WimProcessingService` 是 `IDisposable`；Host 关闭时 DI 容器负责调用 `Dispose()`。

## 注意事项

- 不可多实例：ManagedWimLib 是全局状态库，多个 `WimProcessingService` 实例会引发冲突。
- 所有操作先获取 `_operationLock` 再在 `Task.Run` 中同步执行；`CancellationToken` 通过 WimLib 回调机制透传。
- `ExportImagesAsync` 在 ESD→ISO 流水线中被调用两次：第一次导出 image 2+3 生成 `boot.wim`，第二次导出 image 4..n 生成 `install.wim`。默认 `install.wim` 使用 `Recompress=false`，直接复用官方 solid LZMS 资源（快速路径）；若需重压缩可设 `Recompress=true`。
