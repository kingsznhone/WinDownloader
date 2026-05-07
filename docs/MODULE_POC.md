# WindowsImageDownloader — POC 项目

## 概述

`src/POC` 是 WIM/ISO 控制台验证和对照宿主。主 WinUI 应用已经集成 ESD 到 ISO 转换；POC 直接引用共享的 `WinDownloader.Wim` 和 `WinDownloader.Iso` 项目，用于快速验证转换流水线、进度映射、压缩参数和 oscdimg 行为。

POC 实现自己的 CLI 流水线（`CliConversionService`），而主应用的流水线由 `EsdToIsoConversionService` 负责。两者均依赖相同的共享库：

| 区域 | 说明 |
|------|------|
| `src/WinDownloader` | WinUI 产品入口，负责 ESD 下载和可选 ISO 转换 |
| `src/WinDownloader.Wim` | ManagedWimLib 封装库（见 [MODULE_WIM.md](MODULE_WIM.md)） |
| `src/WinDownloader.Iso` | ISO 打包库，oscdimg 封装（见 [MODULE_ISO.md](MODULE_ISO.md)） |
| `src/POC` | 控制台验证入口，便于调试 WIM/ISO 细节和进度映射 |

## 文件结构

```text
src/POC/
├── POC.csproj
├── Program.cs                           # 最小控制台宿主
├── README.md
├── Interfaces/                          # 空（CLI 服务无需单独接口）
├── Models/
│   ├── CliConversionProgress.cs         # CLI 进度快照
│   └── CliConversionResult.cs           # CLI 转换结果
├── Services/
│   └── CliConversionService.cs          # ESD→ISO CLI 流水线实现
└── Oscdimg/                             # POC 本地 oscdimg.exe 工具
```

## 依赖

| 依赖 | 用途 |
|------|------|
| `WinDownloader.Wim` | WIM/ESD 读取、提取、导出（见 [MODULE_WIM.md](MODULE_WIM.md)） |
| `WinDownloader.Iso` | ISO 创建后端（见 [MODULE_ISO.md](MODULE_ISO.md)） |
| Oscdimg 工具目录 | UDF ISO 创建；`POC.csproj` 只复制 `oscdimg.exe` 到输出目录 |

`Program.cs` 使用 `System.CommandLine` 处理参数，避免手写解析。

## Program.cs 入口

`Program.cs` 只做宿主职责：

1. 解析 `--source`、`--output-root`、`--volume-label`、`--delete-staging`、`--install-compression`、`--recompress-install-image`、`--iso-only`。
2. 创建 `WimProcessingService`、`OscdimgIsoCreationService` 和 `CliConversionService`。
3. 调用 `CliConversionService.ConvertAsync()`，传入 `IProgress<CliConversionProgress>` 回调。
4. 打印最终 `CliConversionResult`，并把控制台输出镜像到 `console-*.log`。

常用命令：

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --delete-staging
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --recompress-install-image
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --install-compression LZX
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc --iso-only
```

参数说明：

| 参数 | 默认 | 说明 |
|------|------|------|
| `--source` | 硬编码本地测试 ESD | 源 ESD 路径 |
| `--output-root` | 源 ESD 同级 `poc-iso-staging` | POC staging root；完整转换会在其下创建 `staging` 子目录 |
| `--volume-label` | `ESD_ISO` | ISO 卷标 |
| `--delete-staging` | 关闭 | 成功后删除 staging 中间文件 |
| `--install-compression` | `LZMS` | `install.wim` 压缩算法；`LZX` 会强制重压 |
| `--recompress-install-image` | 关闭 | 强制重压 `install.wim`，用于速度/体积对比或非默认压缩验证 |
| `--iso-only` | 关闭 | 跳过 WIM/ESD 阶段，只对现有 staging 目录运行 oscdimg |

## 服务边界

### CliConversionService

`CliConversionService` 是 POC 的 ESD→ISO 流水线实现，接收标准 `IProgress<CliConversionProgress>` 回调，适合控制台输出场景：

```text
Program
  -> CliConversionService.ConvertAsync(esdPath, stagingDirectory, isoPath, ...)
    -> IProgress<CliConversionProgress>
      -> WimProcessingService.GetImagesAsync
      -> WimProcessingService.ExtractImageAsync
      -> WimProcessingService.ExportImagesAsync boot.wim
      -> WimProcessingService.ExportImagesAsync install.wim
      -> OscdimgIsoCreationService.CreateIsoAsync
    -> CliConversionResult
```

与主应用 `EsdToIsoConversionService` 的区别：

| 主题 | 主应用 `EsdToIsoConversionService` | POC `CliConversionService` |
|------|------|------|
| 进度推送 | `event EventHandler<EsdToIsoTaskSnapshot>` | `IProgress<CliConversionProgress>` |
| 快照类型 | `EsdToIsoTaskSnapshot`（含阶段、高水位、子进度） | `CliConversionProgress`（数字进度、阶段名、消息） |
| 返回 | `EsdToIsoResult`（含详细分段信息） | `CliConversionResult`（简化字段） |
| DI 注册 | Singleton（主应用 DI） | 手动 new（POC 无 DI） |

### 进度通知

`CliConversionProgress` 包含：

| 字段 | 说明 |
|------|------|
| `Progress` | 归一化进度，范围 `[0, 1]` |
| `Stage` | 当前转换阶段名 |
| `Message` | 中文描述文本 |

## ESD 到 ISO 映像关系

Windows ESD 映像索引约定：

| 源 ESD 映像 | 目标 | 说明 |
|-------------|------|------|
| image 1 | `staging\` | 展开成 ISO 文件树骨架 |
| image 2 | `staging\sources\boot.wim` index 1 | Windows PE |
| image 3 | `staging\sources\boot.wim` index 2 | Windows Setup PE，标记为 bootable |
| image 4..n | `staging\sources\install.wim` | 安装系统版本 |

默认压缩策略：`boot.wim` 使用 LZX；`install.wim` 默认使用 LZMS 且不重压（复用官方 solid LZMS 资源）。使用 `--recompress-install-image` 或 `--install-compression LZX` 可验证重压路径。

## 输出产物

| 文件/目录 | 用途 |
|-----------|------|
| `staging\` | ISO 根目录，中间文件默认保留；传 `--delete-staging` 后成功时删除 |
| `staging\sources\boot.wim` | 由 image 2+3 生成 |
| `staging\sources\install.wim` | 由 image 4..n 生成 |
| `{SourceFileName}.iso` | oscdimg 后端产物，生成在源 ESD 同级目录 |
| `console-*.log` | Program 层控制台输出镜像，生成在 `--output-root` |

## 与主项目的差异

| 主题 | 主 WinUI 项目 | POC |
|------|---------------|-----|
| staging 路径 | 任务目录下 `.staging` | `--output-root` 下的 `staging` 子目录 |
| ISO 输出 | 任务目录下 `{FileNameWithoutExtension}.iso` | 源 ESD 同级 `{FileNameWithoutExtension}.iso` |
| 流水线服务 | `EsdToIsoConversionService`（DI Singleton） | `CliConversionService`（手动 new） |
| 进度机制 | `EventHandler<EsdToIsoTaskSnapshot>` | `IProgress<CliConversionProgress>` |
| 生命周期 | Host 关闭时取消 worker，转换不持久化 | 控制台 Ctrl+C 取消进程内转换 |
| UI | Download task item 显示主/子进度 | 控制台逐行输出快照和诊断信息 |

## 后续验证方向

- 验证 `boot.wim` 和 `install.wim` 的映像索引、boot 标记、压缩和文件大小。
- 验证 oscdimg 产物的挂载结果和 UEFI 虚拟机启动结果。
- 验证大型映像处理的取消、中间文件保留和错误提示。
- 继续验证 POC 和主项目在相同 ESD 上的转换输出一致性。
