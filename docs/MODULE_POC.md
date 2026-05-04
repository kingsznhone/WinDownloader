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
├── Oscdimg/                            # POC 本地 oscdimg 工具和启动映像
└── Wim/
    ├── Interfaces/
    │   ├── IEsdToIsoPipelineService.cs
    │   ├── IIsoCreationService.cs
    │   └── IWimProcessingService.cs
    ├── Models/
    │   ├── EsdToIso*.cs
    │   ├── IsoCreation*.cs
    │   ├── InstallImageFormat.cs
    │   ├── Wim*.cs
    │   └── ...
    └── Services/
        ├── EsdToIsoPipelineService.cs
        ├── OscdimgIsoCreationService.cs
        ├── WimProcessingService.cs
        └── ...
```

## 依赖

| 依赖 | 用途 |
|------|------|
| ManagedWimLib | WIM 读取、提取、导出 |
| Oscdimg 工具目录 | UDF ISO 创建；`POC.csproj` 会复制到输出目录 |

## Program.cs 实验入口

当前 `Program.cs` 硬编码一个本地 ESD 源文件，用于 ESD 到 ISO 后处理实验：

- 启动后先确认源 ESD 存在。
- 通过 `WimProcessingService.GetLibraryInfoAsync()` 输出 wimlib 版本。
- 通过 `GetImagesAsync()` 枚举 ESD 内的映像索引、名称、版本信息和估算大小。
- 执行单个 ESD 到标准 ISO staging 的验证流程，默认保留中间目录和 WIM/ESD 输出。
- 支持 `--inspect-only` 只枚举源 ESD，不执行大体积转换。

常用参数：

```powershell
dotnet run --project .\src\POC\POC.csproj -- --inspect-only
dotnet run --project .\src\POC\POC.csproj -- --install-format esd --iso-backend oscdimg
dotnet run --project .\src\POC\POC.csproj -- --install-format both --iso-backend oscdimg --output-root D:\IsoPoc
```

参数说明：

| 参数 | 值 | 默认 | 说明 |
|------|----|------|------|
| `--source` | ESD 路径 | 硬编码实验 ESD | 覆盖默认源文件 |
| `--output-root` | 目录 | 源 ESD 同级 `poc-iso-output` | POC run 输出根目录 |
| `--install-format` | `esd` / `wim` / `both` | `esd` | 生成 `install.esd`、`install.wim` 或两者 |
| `--iso-backend` | `oscdimg` | `oscdimg` | 选择 ISO 创建后端；当前仅保留 oscdimg |
| `--volume-label` | 卷标 | `ESD_ISO` | ISO 卷标 |
| `--inspect-only` | 无 | 关闭 | 只枚举映像和计划，不转换 |

## ESD 到 ISO 映像关系

当前 POC 使用单个 ESD 的标准映像角色：

| 源 ESD 映像 | 目标 | 说明 |
|-------------|------|------|
| image 1 | `staging\` | 展开成 ISO 文件树骨架 |
| image 2 | `staging\sources\boot.wim` index 1 | Windows PE |
| image 3 | `staging\sources\boot.wim` index 2 | Windows Setup，标记 bootable |
| image 4..n | `staging\sources\install.esd` 或 `install.wim` | 安装系统版本 |

压缩策略固定在 POC 管道中：

- `boot.wim` 使用 `LZX`，避免对启动映像使用高压缩率算法。
- `install.esd` 使用 `LZMS` 和 solid 写入，偏体积。
- `install.wim` 使用 `LZX`，偏兼容和可检查性。
- 普通用户路径不暴露原始压缩算法选择；由输出格式推导。

## ISO 创建后端

| 后端 | 定位 | 注意事项 |
|------|------|----------|
| `oscdimg` | 兼容性目标 | POC 优先调用输出目录中的 `Oscdimg\oscdimg.exe`；使用 UDF 1.02 和 BIOS + UEFI boot data；缺工具时只标记后端 skipped，不删除中间产物 |

每次运行会生成：

| 文件/目录 | 用途 |
|-----------|------|
| `staging\` | ISO 根目录，中间文件保留供探索 |
| `staging\sources\boot.wim` | 由 image 2+3 生成 |
| `staging\sources\install.esd` / `install.wim` | 由 image 4..n 生成 |
| `oscdimg.iso` | oscdimg 后端产物；文件名保持较短以兼容 oscdimg 2.56 的目标路径限制 |
| `events.ndjson` | 阶段和进度事件流 |
| `manifest.json` | 输入、输出、文件大小、后端结果和 warning |
| `summary.txt` | 人类可读摘要 |

## WimProcessingService

当前 POC 中保留 ManagedWimLib 包装能力：

```csharp
Task<WimLibraryInfo> GetLibraryInfoAsync(CancellationToken ct = default);
Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken ct = default);
Task ExtractImageAsync(string imagePath, int imageIndex, string destinationDirectory,
    IProgress<WimOperationProgress>? progress = null, CancellationToken ct = default);
Task ExportImageAsync(WimExportRequest request,
    IProgress<WimOperationProgress>? progress = null, CancellationToken ct = default);
Task ExportImagesAsync(WimMultiImageExportRequest request,
    IProgress<WimOperationProgress>? progress = null, CancellationToken ct = default);
```

实现要点：

- 使用 `SemaphoreSlim(1, 1)` 串行化 WIM 操作。
- 延迟执行 `ManagedWim.GlobalInit()`，优先显式加载 NuGet 输出目录下的 packaged native `libwim`，释放时执行 `TryGlobalCleanup()`。
- 通过 ManagedWimLib callback 映射 `WimOperationProgress`。
- `ExportImagesAsync()` 用于把多个 ESD image 写入同一个目标 WIM/ESD，例如 `boot.wim` 和 `install.esd`。
- `Wim.Write()` 写出目标 WIM/ESD 时使用 `ManagedWim.AllImages`；`0` 是 `NoImage`，会触发 `InvalidImage`。
- 命名空间为 `POC.Wim.*`，避免与主项目模型混用。

## 后续验证方向

- 验证 `boot.wim` 和 `install.esd/install.wim` 的映像索引、boot 标记、压缩和文件大小。
- 验证 oscdimg 产物的挂载结果和 UEFI/BIOS 虚拟机启动结果。
- 验证大型镜像处理的进度、取消、临时文件保留和错误提示。
- POC 跑通后再决定是否以新模块形式回迁主项目。
