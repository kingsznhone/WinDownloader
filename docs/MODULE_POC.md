# WindowsImageDownloader — POC 项目

## 概述

`src/POC` 是后处理概念验证区。WIM/ISO 相关代码仍然留在这里，不参与主 WinUI 应用运行路径。

当前 POC 已从“实验脚手架 + CLI pipeline”收敛为“未来 WinUI 后处理服务的控制台宿主版”：

| 区域 | 说明 |
|------|------|
| `src/WindowsImageDownloader` | ESD-only 下载应用，暂不接入 WIM/ISO 后处理 |
| `src/POC` | ESD 到 ISO 后处理服务形态验证 |

## 文件结构

```text
src/POC/
├── POC.csproj
├── Program.cs                           # 最小控制台宿主
├── README.md
├── Oscdimg/                             # POC 本地 oscdimg 工具和启动映像
├── Interfaces/
│   ├── IEsdToIsoConversionService.cs
│   ├── IIsoCreationService.cs
│   └── IWimProcessingService.cs
├── Models/
│   ├── EsdToIsoRequest.cs
│   ├── EsdToIsoResult.cs
│   ├── EsdToIsoStage.cs
│   ├── EsdToIsoTaskSnapshot.cs
│   ├── EsdToIsoTaskState.cs
│   ├── IsoCreation*.cs
│   ├── Wim*.cs
│   └── ...
└── Services/
    ├── EsdToIsoConversionService.cs
    ├── OscdimgIsoCreationService.cs
    └── WimProcessingService.cs
```

## 依赖

| 依赖 | 用途 |
|------|------|
| ManagedWimLib | WIM/ESD 读取、提取、导出 |
| Oscdimg 工具目录 | UDF ISO 创建；`POC.csproj` 会复制到输出目录 |

`Program.cs` 使用 `System.CommandLine` 处理少量参数，避免手写解析；CLI 仍只作为服务宿主，不参与转换流水线。

## Program.cs 入口

`Program.cs` 只做宿主职责：

1. 解析 `--source`、`--output-root`、`--volume-label`、`--keep-intermediate`、`--delete-intermediate`。
2. 创建 `WimProcessingService`、`OscdimgIsoCreationService` 和 `EsdToIsoConversionService`。
3. 订阅 `EsdToIsoConversionService.ProgressChanged`。
4. 调用 `ConvertAsync()`。
5. 打印最终 `EsdToIsoResult`，并把控制台输出镜像到 `console-*.log`。

`Program.cs` 不再直接枚举 WIM image、不再选择 install 输出格式、不再参与进度计算；log 文件也只保留控制台内容，不承担结构化 manifest 职责。

常用命令：

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj --
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\install.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\install.esd --delete-intermediate
```

参数说明：

| 参数 | 默认 | 说明 |
|------|------|------|
| `--source` | 硬编码本地测试 ESD | 源 ESD 路径 |
| `--output-root` | 源 ESD 同级 `poc-iso-output` | POC run 输出根目录 |
| `--volume-label` | `ESD_ISO` | ISO 卷标 |
| `--keep-intermediate` | 开启 | 保留 staging 中间文件 |
| `--delete-intermediate` | 关闭 | 成功后删除 staging 中间文件 |

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
      -> WimProcessingService.ExportImagesAsync install.esd
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
| `StatusText` | 人类可读状态 |
| `CurrentFile` | 当前处理路径 |
| `ErrorMessage` | 失败或取消原因 |
| `IsoPath` | ISO 输出路径 |
| `WimProgress` | ManagedWimLib 子进度，供 UI 或控制台可选展示 |

发布节流由 service 负责：阶段变化、终态变化、进度超过约 0.5% 或超过约 250ms 才发布。控制台宿主直接订阅该事件并格式化输出；未来 WinUI ViewModel 也可以订阅同一事件并切回 UI 线程更新绑定属性。

## ESD 到 ISO 映像关系

当前 POC 使用单个 ESD 的标准映像角色：

| 源 ESD 映像 | 目标 | 说明 |
|-------------|------|------|
| image 1 | `staging\` | 展开成 ISO 文件树骨架 |
| image 2 | `staging\sources\boot.wim` index 1 | Windows PE |
| image 3 | `staging\sources\boot.wim` index 2 | Windows Setup，标记 bootable |
| image 4..n | `staging\sources\install.esd` | 安装系统版本 |

压缩策略固定在 POC 服务中：

- `boot.wim` 使用 `LZX`。
- `install.esd` 使用 `LZMS` 和 solid 写入。
- 不暴露 `install.wim`、`both` 或原始压缩算法选择。

## ISO 创建后端

| 后端 | 定位 | 注意事项 |
|------|------|----------|
| `oscdimg` | 兼容性目标 | POC 优先调用输出目录中的 `Oscdimg\oscdimg.exe`，再查找 PATH 和 ADK 常见路径；使用 UDF 1.02 和 BIOS + UEFI boot data |

与旧实验期行为不同：未找到 oscdimg、缺启动映像或 oscdimg 退出失败时，`EsdToIsoConversionService` 会把转换标记为 `Failed`，而不是把 ISO 后端标记为 skipped。

每次运行会生成：

| 文件/目录 | 用途 |
|-----------|------|
| `staging\` | ISO 根目录，中间文件默认保留 |
| `staging\sources\boot.wim` | 由 image 2+3 生成 |
| `staging\sources\install.esd` | 由 image 4..n 生成 |
| `oscdimg.iso` | oscdimg 后端产物；文件名保持较短以兼容 oscdimg 2.56 的目标路径限制 |
| `console-*.log` | Program 层控制台输出镜像，生成在 output root |

POC 第一版不再默认写 `events.ndjson`、`manifest.json` 或 `summary.txt`。后续如果需要事件重放或结构化诊断，可作为调试开关重新引入。

## WimProcessingService

当前 POC 中保留 ManagedWimLib 包装能力：

```csharp
Task<IReadOnlyList<WimImageInfo>> GetImagesAsync(string imagePath, CancellationToken ct = default);
Task ExtractImageAsync(string imagePath, int imageIndex, string destinationDirectory,
  Action<WimOperationProgress>? progress = null, CancellationToken ct = default);
Task ExportImagesAsync(WimExportRequest request,
  Action<WimOperationProgress>? progress = null, CancellationToken ct = default);
```

实现要点：

- 使用 `SemaphoreSlim(1, 1)` 串行化 WIM 操作。
- 延迟执行 `ManagedWim.GlobalInit()`，优先显式加载 NuGet 输出目录下的 packaged native `libwim`，释放时执行 `TryGlobalCleanup()`。
- 通过 ManagedWimLib callback 同步回调 `WimOperationProgress`。
- `ExportImagesAsync()` 用于把多个 ESD image 写入同一个目标 WIM/ESD，例如 `boot.wim` 和 `install.esd`。
- `Wim.Write()` 写出目标 WIM/ESD 时使用 `ManagedWim.AllImages`；`0` 是 `NoImage`，会触发 `InvalidImage`。

## 后续验证方向

- 验证 `boot.wim` 和 `install.esd` 的映像索引、boot 标记、压缩和文件大小。
- 验证 oscdimg 产物的挂载结果和 UEFI/BIOS 虚拟机启动结果。
- 验证大型镜像处理的 snapshot 频率、取消、临时文件保留和错误提示。
- POC 服务形态跑通后，再决定是否以新模块形式回迁主项目。
