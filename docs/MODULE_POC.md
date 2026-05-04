# WindowsImageDownloader — POC 项目

## 概述

`src/POC` 是后处理概念验证区。WIM/ISO 相关代码已从主 WinUI 项目剥离到这里，避免主应用在 ESD 下载流程稳定前承载转换复杂度。

主项目与 POC 的边界：

| 区域 | 说明 |
|------|------|
| `src/WindowsImageDownloader` | ESD-only 下载应用 |
| `src/POC` | WIM/ISO 后处理实验，不参与主应用运行路径 |

## 文件结构

```text
src/POC/
├── POC.csproj
├── Program.cs
├── README.md
└── Wim/
    ├── Interfaces/IWimProcessingService.cs
    ├── Models/
    │   ├── WimCompressionKind.cs
    │   ├── WimExportRequest.cs
    │   ├── WimImageInfo.cs
    │   ├── WimLibraryInfo.cs
    │   └── WimOperationProgress.cs
    └── Services/WimProcessingService.cs
```

## 依赖

| 依赖 | 用途 |
|------|------|
| ManagedWimLib | WIM 读取、提取、导出 |
| DiscUtils | ISO 创建实验预留 |

## WimProcessingService

当前 POC 中保留 ManagedWimLib 包装能力：

```csharp
Task<WimLibraryInfo> GetLibraryInfoAsync(CancellationToken ct = default);
Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken ct = default);
Task ExtractImageAsync(string imagePath, int imageIndex, string destinationDirectory,
    IProgress<WimOperationProgress>? progress = null, CancellationToken ct = default);
Task ExportImageAsync(WimExportRequest request,
    IProgress<WimOperationProgress>? progress = null, CancellationToken ct = default);
```

实现要点：

- 使用 `SemaphoreSlim(1, 1)` 串行化 WIM 操作。
- 延迟执行 `ManagedWim.GlobalInit()`，释放时执行 `TryGlobalCleanup()`。
- 通过 ManagedWimLib callback 映射 `WimOperationProgress`。
- 命名空间为 `POC.Wim.*`，避免与主项目模型混用。

## 后续验证方向

- 明确 ESD 到 WIM 的输入、输出、失败恢复和权限要求。
- 评估 ISO 创建方案和 DiscUtils 能力边界。
- 验证大型镜像处理的进度、取消、临时文件清理和错误提示。
- POC 跑通后再决定是否以新模块形式回迁主项目。
