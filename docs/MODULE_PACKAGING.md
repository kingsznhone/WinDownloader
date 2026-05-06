# WindowsImageDownloader — 打包与发布

## 当前打包策略

主项目使用非 MSIX 解包部署模式，依赖 Windows App SDK self-contained 发布。

关键配置位于 `src/WindowsImageDownloader/WindowsImageDownloader.csproj`：

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<EnableMsixTooling>false</EnableMsixTooling>
<PublishAot>false</PublishAot>
<PublishReadyToRun>false</PublishReadyToRun>
<PublishTrimmed>false</PublishTrimmed>
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
<Platforms>x64</Platforms>
```

## 构建

```powershell
dotnet build .\src\WindowsImageDownloader\WindowsImageDownloader.csproj -nologo -p:Platform=x64 -v minimal
```

主项目只声明 `x64`/`win-x64`，Windows App SDK 构建需要显式平台参数；省略 `-p:Platform=x64` 会触发不支持的架构错误。

## 发布

```powershell
dotnet publish src/WindowsImageDownloader/WindowsImageDownloader.csproj -c Release -r win-x64 --self-contained true
```

输出目录：

```text
src/WindowsImageDownloader/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/
```

## 主项目依赖

| 依赖 | 用途 |
|------|------|
| CommunityToolkit.Mvvm | MVVM source generator 和基础类型 |
| CommunityToolkit.WinUI.Controls.SettingsControls | 设置页控件 |
| Downloader | HTTP 多线程断点续传 |
| Microsoft.Data.Sqlite | SQLite 任务缓存 |
| Microsoft.Extensions.DependencyInjection | DI 容器 |
| Microsoft.Extensions.Hosting | Host 和 `IHostedService` 生命周期 |
| Microsoft.Windows.SDK.BuildTools | Windows SDK 构建工具 |
| Microsoft.WindowsAppSDK | WinUI 3 / Windows App SDK |
| WindowsImageDownloader.Wim | ManagedWimLib 封装、WIM/ESD 操作 |
| WindowsImageDownloader.Iso | ESD→ISO 流水线和 oscdimg 后端 |

主项目还包含 `Oscdimg\oscdimg.exe` copy-to-output 规则：

```xml
<None Update="Oscdimg\oscdimg.exe">
	<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

`Oscdimg` 目录只需要随输出复制 `oscdimg.exe`。ISO 的 EFI 启动映像来自 ESD image 1 展开的 staging 文件树：`efi\microsoft\boot\efisys.bin`；项目不再发布预置 `efisys*.bin` 或 `etfsboot.com`。

## POC 依赖

`src/POC/POC.csproj` 是控制台宿主，直接引用共享转换库：

| 依赖 | 用途 |
|------|------|
| WindowsImageDownloader.Wim | WIM 读取、导出、提取验证 |
| WindowsImageDownloader.Iso | ESD→ISO 流水线验证 |
| Oscdimg 工具目录 | POC ISO 创建验证；构建时只复制 `oscdimg.exe` 到 POC 输出目录 |

## 安装要求

- Windows 10 19041+ 或 Windows 11。
- `expand.exe` 为 Windows 内置组件，用于解压产品目录 CAB。
- ISO 转换依赖应用输出目录中的 `Oscdimg\oscdimg.exe`、ESD 展开的 EFI 启动映像和由 `WindowsImageDownloader.Wim` 携带的 ManagedWimLib native `libwim`。
- 主应用发布为 Windows App SDK self-contained，但仍按目标机器环境验证运行时兼容性。

## 注意事项

- 当前没有 MSIX 包签名或自动更新机制。
- 程序数据位于 `%LocalAppData%\WindowsImageDownloader\`。
- `app.manifest` 包含长路径支持。
- POC 项目不参与主应用发布流程；主项目已经自行携带 ISO 转换所需依赖。
