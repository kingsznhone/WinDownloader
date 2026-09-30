# WindowsImageDownloader — ISO 创建库模块

## 概述

`WinDownloader.Iso` 调用 `oscdimg.exe`，将就绪的 staging 目录打包为 UEFI ISO。不读取 ESD、不操作 WIM；staging 由主应用或 POC 的转换流水线准备。

## 项目信息

| 属性 | 值 |
|------|------|
| 项目文件 | `src/WinDownloader.Iso/WinDownloader.Iso.csproj` |
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

- 每次调用启动独立进程，无状态，可作为 Singleton 注册，无需 dispose。
- 构造时在 `AppContext.BaseDirectory` 查找 `oscdimg.exe`；找不到立即抛出 `InvalidOperationException`。
- staging 必须包含 `efi\microsoft\boot\efisys.bin`（来自 ESD image 1）；缺失时返回失败和警告。
- 通过解析 oscdimg **标准错误** 输出提取进度百分比，经 `IsoCreationRequest.OnProgress` 回调通知调用方。
- 取消时调用 `Process.Kill(entireProcessTree: true)`，避免 oscdimg 子进程残留。
- 若 `OutputIsoPath` 已存在，创建前自动删除旧文件；输出目录不存在时自动创建。

## 模型

### IsoCreationRequest

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

### IsoOperationProgress

`Percent` 范围 0-100，从 stderr 解析；转换流水线负责映射到整体进度。

## oscdimg 工具说明

工具随构建复制到输出目录；发布资产规则见 [打包模块](MODULE_PACKAGING.md)。

## 消费方

| 消费方 | 位置 | 说明 |
|------|------|------|
| `EsdToIsoConversionService` | `WinDownloader/Services/` | 主应用 ESD→ISO 转换流水线 |
| `CliConversionService` | `POC/Services/` | POC 控制台 ESD→ISO 转换流水线 |

## 注意事项

- 失败或取消可能留下半成品 ISO；本库不主动删除，staging 清理由上层决定。
