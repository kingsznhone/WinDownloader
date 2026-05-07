# WindowsImageDownloader — ISO 创建库模块

## 概述

`WinDownloader.Iso` 是纯 ISO 打包库，封装 `oscdimg.exe` 命令行工具，将准备好的 staging 目录转换为可启动 UEFI ISO 镜像文件。**该库只负责 ISO 打包**，不包含 ESD 读取或 WIM 操作；ESD→ISO 流水线的编排由上层消费方实现（主应用 `EsdToIsoConversionService`、POC `CliConversionService`）。

## 项目信息

| 属性 | 值 |
|------|------|
| 项目文件 | `src/WinDownloader.Iso/WinDownloader.Iso.csproj` |
| 命名空间 | `WinDownloader.Iso`、`WinDownloader.Iso.Interfaces` |
| 目标框架 | `net10.0` |
| 主要依赖 | `oscdimg.exe`（随项目输出复制，无 NuGet 依赖） |

## 文件结构

```text
src/WinDownloader.Iso/
├── WinDownloader.Iso.csproj
├── oscdimg.exe                          # 随构建复制到输出目录
├── Interfaces/
│   └── IIsoCreationService.cs           # 服务接口
├── Models/
│   ├── IsoCreationRequest.cs            # ISO 创建请求
│   ├── IsoCreationResult.cs             # ISO 创建结果
│   └── IsoOperationProgress.cs          # 进度（0–100 百分比）
└── Services/
    └── OscdimgIsoCreationService.cs     # oscdimg.exe 后端实现
```

## IIsoCreationService

```csharp
Task<IsoCreationResult> CreateIsoAsync(
    IsoCreationRequest request,
    CancellationToken cancellationToken = default);
```

## OscdimgIsoCreationService

- **无状态**，可安全作为 Singleton 注册。
- 构造时在 `AppContext.BaseDirectory` 查找 `oscdimg.exe`；找不到立即抛出 `InvalidOperationException`。
- 调用前检查 staging 目录中是否存在 `efi\microsoft\boot\efisys.bin`；缺失时将警告写入结果 `Warnings` 并返回空参数列表（导致失败，不会意外创建无 UEFI 启动能力的 ISO）。
- 通过解析 oscdimg **标准错误** 输出提取进度百分比，经 `IsoCreationRequest.OnProgress` 回调通知调用方。
- 取消时调用 `Process.Kill(entireProcessTree: true)`，避免 oscdimg 子进程残留。
- 若 `OutputIsoPath` 已存在，创建前自动删除旧文件；输出目录不存在时自动创建。

## 模型

### IsoCreationRequest

```csharp
record IsoCreationRequest(
    string StagingDirectory,    // ISO staging 根目录
    string OutputIsoPath,       // 输出 ISO 文件完整路径
    string VolumeLabel)         // ISO 卷标（最长 32 字符；空时默认 "ESD_ISO"）
{
    Action<IsoOperationProgress>? OnProgress { get; init; }  // 进度回调，可选
}
```

| 字段 | 说明 |
|------|------|
| `StagingDirectory` | 包含 `efi`、`sources` 等子目录的 staging 根目录 |
| `OutputIsoPath` | 输出 ISO 文件完整路径 |
| `VolumeLabel` | ISO 卷标（超过 32 字符会截断；空时使用 `ESD_ISO`） |
| `OnProgress` | 可选进度回调 |

### IsoCreationResult

| 字段 | 说明 |
|------|------|
| `Succeeded` | `true` 当且仅当 oscdimg 退出码为 0 且输出文件存在 |
| `Duration` | oscdimg 进程执行耗时 |
| `OutputSize` | 成功时输出 ISO 文件字节数 |
| `ToolPath` / `CommandLine` | oscdimg 路径和完整命令行（供调试） |
| `ExitCode` | oscdimg 进程退出码 |
| `StandardOutput` / `StandardError` | 进程完整输出（供诊断） |
| `Warnings` | 非致命警告列表（如缺少 efisys.bin） |
| `ErrorMessage` | 失败时的描述文本；成功时为 `null` |

`IsoCreationResult.Failure(outputIsoPath, message, warnings)` 静态工厂方法用于快速构造失败结果。

### IsoOperationProgress

```csharp
record IsoOperationProgress(double Percent);
```

百分比 0–100，从 oscdimg stderr 解析。上层消费方负责将该值映射到全局进度区间（例如主应用将 oscdimg 阶段映射到 86%–100%）。

## oscdimg 工具说明

oscdimg.exe 是微软官方工具，用于创建 UDF/ISO 9660 可启动 ISO 镜像，支持 UEFI 启动。

- 工具存放在项目根目录，构建时通过 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` 随输出复制。
- UEFI 启动依赖 staging 中的 `efi\microsoft\boot\efisys.bin`；该文件由 ESD image 1 提取时一并产生。
- 参数由 `CreateArguments` 方法根据 staging 内容和请求参数动态构建。

## DI 注册

```text
IIsoCreationService    Singleton    OscdimgIsoCreationService
```

服务无状态，无需 dispose。

## 消费方

| 消费方 | 位置 | 说明 |
|------|------|------|
| `EsdToIsoConversionService` | `WinDownloader/Services/` | 主应用 ESD→ISO 转换流水线 |
| `CliConversionService` | `POC/Services/` | POC 控制台 ESD→ISO 转换流水线 |

两个消费方各自负责准备 staging 目录（通过 `WinDownloader.Wim`）后，再调用 `IIsoCreationService.CreateIsoAsync` 执行最后的 ISO 打包步骤。

## 注意事项

- 该库**不依赖** `WinDownloader.Wim`；ESD 解包和 WIM 导出由调用方负责，Iso 库只处理已就绪的 staging 目录。
- 失败时不主动删除半成品 ISO；上层消费方（`EsdToIsoConversionService`、`CliConversionService`）负责清理 staging 和部分产物。
- 每次 `CreateIsoAsync` 启动新的 oscdimg 进程，服务本身无内部状态，线程安全。
