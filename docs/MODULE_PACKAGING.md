# WindowsImageDownloader — 打包与发布

## 当前打包策略

主项目使用非 MSIX 解包部署模式，依赖 Windows App SDK self-contained 发布。

关键配置位于 `src/WinDownloader/WinDownloader.csproj`：

```xml
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<EnableMsixTooling>false</EnableMsixTooling>
<PublishAot>false</PublishAot>
<PublishReadyToRun>false</PublishReadyToRun>
<PublishTrimmed>false</PublishTrimmed>
<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
<Platforms>x64</Platforms>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

`AllowUnsafeBlocks` 用于支持主窗口 `LibraryImport` 生成的本机调用代码，包括图标设置和 DPI 查询。

## 构建

```powershell
dotnet build .\src\WinDownloader\WinDownloader.csproj -nologo -p:Platform=x64 -v minimal
```

主项目只声明 `x64`/`win-x64`，Windows App SDK 构建需要显式平台参数；省略 `-p:Platform=x64` 会触发不支持的架构错误。

构建生成 `WinDownloader.pri`，发布 target 复制 PRI/XBF；资源规则和验证见 [本地化模块](MODULE_LOCALIZATION.md#pri-构建行为)。

## 发布

```powershell
dotnet publish src/WinDownloader/WinDownloader.csproj -c Release -r win-x64 --self-contained true
```

输出目录：

```text
src/WinDownloader/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish/
```

## 主项目依赖

| 依赖 | 当前版本 | 用途 |
|------|----------|------|
| CommunityToolkit.Mvvm | 8.4.2 | MVVM source generator 和基础类型 |
| CommunityToolkit.WinUI.Controls.SettingsControls | 8.2.251219 | 设置页控件 |
| Downloader | 5.9.8 | HTTP 多线程断点续传 |
| Microsoft.Data.Sqlite | 10.0.12 | SQLite 任务缓存 |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | DI 容器 |
| Microsoft.Extensions.Hosting | 10.0.12 | Host 和 `IHostedService` 生命周期 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.2705 | Windows SDK 构建工具 |
| Microsoft.WindowsAppSDK | 2.5.1 | WinUI 3 / Windows App SDK |
| WinDownloader.Wim | 项目引用 | ManagedWimLib 封装、WIM/ESD 操作 |
| WinDownloader.Iso | 项目引用 | ISO 打包库和 oscdimg 后端 |

发布需包含 `oscdimg.exe` 和 ManagedWimLib 原生库。EFI 启动映像从 ESD image 1 的 `efi\microsoft\boot\efisys.bin` 提取，不发布预置 `efisys*.bin` 或 `etfsboot.com`。

## POC 依赖

POC 引用相同 WIM/ISO 库，构建时复制 oscdimg，不参与主应用发布；用法见 [POC 模块](MODULE_POC.md)。

## 安装要求

- 运行与开发环境见 [README_ZH.md](../README_ZH.md)。
- `expand.exe` 为 Windows 内置组件，用于解压产品目录 CAB。
- ISO 转换依赖应用输出目录中的 oscdimg 工具、ESD 展开的 EFI 启动映像和由 `WinDownloader.Wim` 携带的 ManagedWimLib native `libwim`。

## 注意事项

- 当前没有 MSIX 包签名或自动更新机制。
- 程序数据位于 `%LocalAppData%\WindowsImageDownloader\`。
- `app.manifest` 包含长路径支持。
