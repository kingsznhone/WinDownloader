# WindowsImageDownloader — POC 项目

## 概述

`src/POC` 是 WIM/ISO 控制台验证和对照宿主。主 WinUI 应用已经集成 ESD 到 ISO 转换；POC 直接引用共享的 `WindowsImageDownloader.Wim` 和 `WindowsImageDownloader.Iso` 项目，用于快速验证同一套转换流水线、进度映射、压缩参数和 oscdimg 行为。

当前 POC 已从“实验脚手架 + CLI pipeline”收敛为“主项目转换服务的控制台对照版”：

| 区域 | 说明 |
|------|------|
| `src/WindowsImageDownloader` | WinUI 产品入口，负责 ESD 下载和可选 ISO 转换 |
| `src/WindowsImageDownloader.Wim` | ManagedWimLib 封装库 |
| `src/WindowsImageDownloader.Iso` | ESD→ISO 流水线和 oscdimg 后端库 |
| `src/POC` | 控制台验证入口，便于调试 WIM/ISO 细节和进度映射 |

## 文件结构

```text
src/POC/
├── POC.csproj
├── Program.cs                           # 最小控制台宿主
├── README.md
└── Oscdimg/                             # POC 本地 oscdimg.exe 工具
```

## 依赖

| 依赖 | 用途 |
|------|------|
| `WindowsImageDownloader.Wim` | WIM/ESD 读取、提取、导出 |
| `WindowsImageDownloader.Iso` | ESD→ISO 流水线和 ISO 创建后端 |
| Oscdimg 工具目录 | UDF ISO 创建；`POC.csproj` 只复制 `oscdimg.exe` 到输出目录 |

`Program.cs` 使用 `System.CommandLine` 处理少量参数，避免手写解析；CLI 仍只作为服务宿主，不参与转换流水线。

## Program.cs 入口

`Program.cs` 只做宿主职责：

1. 解析 `--source`、`--output-root`、`--volume-label`、`--delete-staging`、`--install-compression`、`--reuse-install-resources`、`--recompress-install-image`、`--iso-only`。
2. 创建 `WimProcessingService`、`OscdimgIsoCreationService` 和 `EsdToIsoConversionService`。
3. 订阅 `EsdToIsoConversionService.ProgressChanged`。
4. 调用 `ConvertAsync()`。
5. 打印最终 `EsdToIsoResult`，并把控制台输出镜像到 `console-*.log`。

`Program.cs` 不再直接枚举 WIM image、不再选择 install 输出格式、不再参与进度计算；log 文件也只保留控制台内容，不承担结构化 manifest 职责。

常用命令：

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj --
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
| `--reuse-install-resources` | 开启 | 构建 `install.wim` 时复用官方 solid LZMS ESD 资源 |
| `--recompress-install-image` | 关闭 | 强制重压 `install.wim`，用于速度/体积对比或非默认压缩验证 |
| `--iso-only` | 关闭 | 跳过 WIM/ESD 阶段，只对现有 staging 目录运行 oscdimg |

默认路径已经是复用官方 solid LZMS 资源写入 `install.wim`。如需对比旧式重压缩路径，使用 `--recompress-install-image`，再对比控制台中的 `Duration`、`install.wim size` 和最终 ISO 行为。快速路径要求 `--install-compression LZMS`；如果指定 `LZX`，POC 会强制重压。

## 服务边界

### EsdToIsoConversionService

`EsdToIsoConversionService` 是 POC 的唯一产品层服务入口：

```text
Program
  -> EsdToIsoConversionService.ConvertAsync(request)
    -> ProgressChanged(EsdToIsoTaskSnapshot)
      -> WimProcessingService.GetImagesAsync
      -> WimProcessingService.ExtractImageAsync
      -> WimProcessingService.ExportImagesAsync boot.wim
      -> WimProcessingService.ExportImagesAsync install.wim
      -> OscdimgIsoCreationService.CreateIsoAsync
```

该服务内部维护转换状态，并通过不可变 `EsdToIsoTaskSnapshot` 对外发布变化，模拟主程序未来的：

```text
background worker -> snapshot -> ViewModel coalescing -> UI thread
```

### 进度通知

`EsdToIsoTaskSnapshot` 包含：

| 字段 | 说明 |
|------|------|
| `TaskId` / `SourceEsdPath` | 转换任务标识和输入 |
| `State` | `NotStarted` / `Running` / `Completed` / `Failed` / `Canceled` |
| `Stage` | 当前转换阶段 |
| `Progress` | 归一化进度，范围 `[0, 1]` |
| `CurrentFile` | 当前处理路径 |
| `ErrorMessage` | 失败或取消原因 |
| `IsoPath` | ISO 输出路径 |
| `WimProgress` | ManagedWimLib 子进度，供 UI 或控制台可选展示 |
| `IsoProgress` | oscdimg 子进度，供 UI 或控制台可选展示 |

发布节流由 service 负责：阶段变化、终态变化、进度超过约 0.5% 或超过约 250ms 才发布。控制台宿主直接订阅该事件并格式化输出；WinUI 主项目通过 orchestrator 合并快照，再切回 UI 线程更新绑定属性。

整体进度必须单调递增。`EsdToIsoConversionService` 会对 `Running` 快照做高水位保护，避免 ManagedWimLib 在 `Metadata`、`WriteMetadataBegin/End` 等无百分比回调中把整体百分比打回外层阶段起点。WIM 子阶段按外层阶段折算：

| 外层阶段 | 整体区间 | 子阶段折算 |
|----------|----------|------------|
| `ApplyingSetupMedia` | `8%` - `30%` | `Extracting 0%..100%` 线性映射到完整区间 |
| `BuildingBootWim` | `30%` - `50%` | 映像元数据登记最多占该区间前 `2%`，`Writing` 占 `2%..88%`，`Verifying` 占 `88%..100%` |
| `BuildingInstallImage` | `50%` - `86%` | 映像元数据登记最多占该区间前 `2%`，`Writing` 占 `2%..88%`，`Verifying` 占 `88%..100%` |
| `CreatingIso` | `86%` - `100%` | oscdimg 百分比线性映射 |

后置元数据处理没有可靠百分比时只更新当前项并保持当前高水位；这样控制台和 WinUI UI 都不会出现 `81% -> 50%` 或 `47% -> 30%` 这类回跳。

## ESD 到 ISO 映像关系

当前 POC 使用单个 ESD 的标准映像角色：

| 源 ESD 映像 | 目标 | 说明 |
|-------------|------|------|
| image 1 | `staging\` | 展开成 ISO 文件树骨架 |
| image 2 | `staging\sources\boot.wim` index 1 | Windows PE |
| image 3 | `staging\sources\boot.wim` index 2 | Windows Setup，标记 bootable |
| image 4..n | `staging\sources\install.wim` | 安装系统版本 |

主项目默认压缩策略与 POC 默认值一致：

- `boot.wim` 使用 `LZX`。
- `install.wim` 默认使用 `LZMS`、solid，并复用官方 ESD 中已有资源，避免强制重压。
- POC 可通过 `--recompress-install-image` 或 `--install-compression LZX` 验证重压路径；主应用暂不暴露格式或压缩选择。

## ISO 创建后端

| 后端 | 定位 | 注意事项 |
|------|------|----------|
| `oscdimg` | 兼容性目标 | 调用输出目录中的 `Oscdimg\oscdimg.exe`，使用 ISO+UDF 1.02 和 staging 中的 `efi\microsoft\boot\efisys.bin` 生成单 EFI 启动项 |

与旧实验期行为不同：未找到 oscdimg、缺启动映像或 oscdimg 退出失败时，`EsdToIsoConversionService` 会把转换标记为 `Failed`，而不是把 ISO 后端标记为 skipped。

每次运行会生成：

| 文件/目录 | 用途 |
|-----------|------|
| `staging\` | ISO 根目录，中间文件默认保留；传 `--delete-staging` 后成功时删除 |
| `staging\sources\boot.wim` | 由 image 2+3 生成 |
| `staging\sources\install.wim` | 由 image 4..n 生成 |
| `{SourceFileName}.iso` | oscdimg 后端产物，生成在源 ESD 同级目录 |
| `console-*.log` | Program 层控制台输出镜像，生成在 `--output-root` |

POC 第一版不再默认写 `events.ndjson`、`manifest.json` 或 `summary.txt`。后续如果需要事件重放或结构化诊断，可作为调试开关重新引入。

## WimProcessingService

`WindowsImageDownloader.Wim` 提供 ManagedWimLib 包装能力：

```csharp
Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken ct = default);
Task ExtractImageAsync(WimExtractRequest request,
  Action<WimOperationProgress>? progress = null, CancellationToken ct = default);
Task ExportImagesAsync(WimExportRequest request,
  Action<WimOperationProgress>? progress = null, CancellationToken ct = default);
```

实现要点：

- 使用 `SemaphoreSlim(1, 1)` 串行化 WIM 操作。
- 构造时执行 `ManagedWim.GlobalInit()`，优先显式加载 NuGet 输出目录下的 packaged native `libwim`，释放时执行 `TryGlobalCleanup()`。
- 通过 ManagedWimLib callback 同步回调 `WimOperationProgress`。
- `ExportImagesAsync()` 用于把多个 ESD image 写入同一个目标 WIM，例如 `boot.wim` 和 `install.wim`。
- `Wim.Write()` 写出目标 WIM/ESD 时使用 `ManagedWim.AllImages`；`0` 是 `NoImage`，会触发 `InvalidImage`。

## 与主项目的差异

| 主题 | 主 WinUI 项目 | POC |
|------|---------------|-----|
| staging 路径 | 直接使用任务目录下 `.staging` | 使用 `--output-root` 下的 `staging` 子目录 |
| ISO 输出 | 任务目录下 `{FileNameWithoutExtension}.iso` | 源 ESD 同级 `{FileNameWithoutExtension}.iso` |
| 生命周期 | Host 关闭时取消 worker，转换不持久化 | 控制台 Ctrl+C 取消进程内转换 |
| UI | Download task item 显示主/子进度 | 控制台逐行输出快照和诊断信息 |

## 后续验证方向

- 验证 `boot.wim` 和 `install.wim` 的映像索引、boot 标记、压缩和文件大小。
- 验证 oscdimg 产物的挂载结果和 UEFI 虚拟机启动结果。
- 验证大型镜像处理的 snapshot 频率、取消、临时文件保留和错误提示。
- 继续验证 POC 和主项目在相同 ESD 上的进度映射一致性。
