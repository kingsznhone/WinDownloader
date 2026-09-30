# WindowsImageDownloader — POC 项目

## 概述

`src/POC` 通过独立的 `CliConversionService` 验证共享 WIM/ISO 库、进度映射、压缩参数和 oscdimg 输出，不依赖 WinUI 或任务缓存。

## 文件结构

```text
src/POC/
├── POC.csproj
├── Program.cs                           # 最小控制台宿主
├── README.md
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

`Program.cs` 使用 `System.CommandLine` 解析参数，创建服务、输出进度与结果，并把控制台输出镜像到 `console-*.log`。

## Program.cs 入口

常用命令：

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --recompress-install-image
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
| `--reuse-install-resources` | 开启 | 复用官方 solid LZMS 资源；关闭时重压安装映像 |
| `--recompress-install-image` | 关闭 | 强制重压 `install.wim`，用于速度/体积对比或非默认压缩验证 |
| `--iso-only` | 关闭 | 跳过 WIM/ESD 阶段，只对现有 staging 目录运行 oscdimg |

## ESD 到 ISO 映像关系

映像索引遵循共享库的 [ESD 布局约定](MODULE_WIM.md#esd-镜像分布约定)：image 1 展开安装媒体，2+3 生成 `boot.wim`，4..n 生成 `install.wim`。

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
| 进度内容 | 整体进度、阶段高水位及子进度 | `[0, 1]` 进度、阶段名、描述文本 |
| 生命周期 | Host 关闭时取消 worker，转换不持久化 | 控制台 Ctrl+C 取消进程内转换 |
| UI | Download task item 显示主/子进度 | 控制台逐行输出快照和诊断信息 |

## 后续验证方向

- 验证 `boot.wim` 和 `install.wim` 的映像索引、boot 标记、压缩和文件大小。
- 验证 oscdimg 产物的挂载结果和 UEFI 虚拟机启动结果。
- 验证大型映像处理的取消、中间文件保留和错误提示。
- 继续验证 POC 和主项目在相同 ESD 上的转换输出一致性。
