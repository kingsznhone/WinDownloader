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
dotnet build src/WindowsImageDownloader.slnx
```

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

主项目不引用 ManagedWimLib 或 DiscUtils。

## POC 依赖

`src/POC/POC.csproj` 可引用后处理实验依赖：

| 依赖 | 用途 |
|------|------|
| ManagedWimLib | WIM 读取、导出、提取实验 |
| DiscUtils | ISO 创建实验预留 |

## 安装要求

- Windows 10 19041+ 或 Windows 11。
- `expand.exe` 为 Windows 内置组件，用于解压产品目录 CAB。
- 主应用发布为 Windows App SDK self-contained，但仍按目标机器环境验证运行时兼容性。

## 注意事项

- 当前没有 MSIX 包签名或自动更新机制。
- 程序数据位于 `%LocalAppData%\WindowsImageDownloader\`。
- `app.manifest` 包含长路径支持。
- POC 项目不参与主应用发布流程。
