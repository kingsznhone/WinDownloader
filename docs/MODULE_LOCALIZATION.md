# WindowsImageDownloader — 本地化模块

## 概述

主 WinUI 应用使用 Windows App SDK / MRT Core 的 `.resw` 字符串资源本地化静态 XAML 文本。当前只维护 `en-US` 和 `zh-CN` 两套 UI 资源，语言切换后需要重启应用生效。

本项目是非 MSIX 解包部署，但主项目仍由 WinUI / Windows App SDK MSBuild 管线生成应用 PRI。正常 `dotnet build` 会把 `Strings/{language}/Resources.resw` 编入输出目录中的 `WinDownloader.pri`，不需要手动运行 `MakePri.exe`，也不需要额外生成独立的 `resources.pri`。`dotnet publish` 阶段通过项目内 MSBuild target 自动把已生成的 PRI 和 XBF 文件复制到发布目录。

## 文件清单

| 文件 | 说明 |
|------|------|
| `src/WinDownloader/Strings/en-US/Resources.resw` | 英文字符串资源 |
| `src/WinDownloader/Strings/zh-CN/Resources.resw` | 简体中文字符串资源 |
| `src/WinDownloader/App.xaml.cs` | 启动时应用语言覆盖，必须早于 `InitializeComponent()` |
| `src/WinDownloader/Services/AppSettingsService.cs` | 保存和解析 `AppLanguage` |
| `src/WinDownloader/ViewModels/SettingsViewModel.cs` | 语言选择和重启应用命令 |
| `src/WinDownloader/Views/Pages/SettingsPage.xaml` | 语言设置、重启卡片和设置页静态文本 |
| `src/WinDownloader/**/*.xaml` | 使用 `x:Uid` 绑定静态 UI 文本 |
| `src/WinDownloader/WinDownloader.csproj` | publish 后复制 `WinDownloader.pri` 和 `.xbf` |

## 当前范围

已本地化：

- 主窗口导航项。
- 页面标题、筛选器标题、空状态、错误标题、常见按钮。
- 设置页分组标题、设置卡片标题/说明、语言选项、重启按钮、重置按钮。
- 目录条目和下载任务控件里的少量静态操作按钮。

暂不本地化：

- 产品目录中的语言、版本、文件名和 edition 等数据字段。
- ViewModel 生成的动态状态、错误消息、下载进度和 ISO 转换阶段文本。
- 清单显示名称、安装包元数据和系统 shell 集成文本。

## 资源命名约定

XAML 使用 `x:Uid` 引用资源，资源键使用 `{Uid}.{Property}` 形式：

```xml
<TextBlock x:Uid="Download_Title" Text="下载任务" />
```

对应资源：

```xml
<data name="Download_Title.Text" xml:space="preserve">
  <value>Downloads</value>
</data>
```

常见属性后缀：

| XAML 类型 | 常用资源键 |
|-----------|------------|
| `TextBlock` | `{Uid}.Text` |
| `Button` / `ComboBoxItem` / `NavigationViewItem` | `{Uid}.Content` |
| `ComboBox` | `{Uid}.Header` |
| `InfoBar` | `{Uid}.Title` |
| `ctk:SettingsCard` | `{Uid}.Header`、`{Uid}.Description` |
| `TextBox` | `{Uid}.PlaceholderText` |

资源键必须在 `en-US` 和 `zh-CN` 两个 `Resources.resw` 中同时维护，避免某个语言缺失时退回到硬编码文本或默认语言。

## 语言选择和重启

`AppSettingsService.AppLanguage` 只接受：

| 值 | 行为 |
|----|------|
| `null` | 跟随系统；系统 UI 语言为中文时使用 `zh-CN`，其他语言回退 `en-US` |
| `en-US` | 强制英文 |
| `zh-CN` | 强制简体中文 |

启动时 `App` 会先创建 `AppSettingsService`，再调用 `ApplyLanguageOverride()`，最后才调用 `InitializeComponent()`。这保证 XAML 加载前已经设置 `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` 和 .NET `CultureInfo`。

设置页的语言选择会立即保存到 JSON，但不会实时刷新已经加载的 WinUI 页面。用户需要点击语言卡片下方的“重启应用”卡片，或手动关闭后重新打开应用。

重启命令使用 `Environment.ProcessPath` 启动当前 exe，然后调用 `App.MainWindow.Close()` 走现有 Host 停止流程。

## PRI 构建行为

官方文档对裸 MakePri 工作流会建议手动生成 `resources.pri` 并复制到 exe 目录。但本项目是 SDK-style WinUI 项目，具备这些关键配置：

```xml
<UseWinUI>true</UseWinUI>
<WinUISDKReferences>true</WinUISDKReferences>
<WindowsPackageType>None</WindowsPackageType>
<PackageReference Include="Microsoft.Windows.SDK.BuildTools" ... />
<PackageReference Include="Microsoft.WindowsAppSDK" ... />
```

因此 MSBuild 会在构建时自动处理 `.resw`，输出应用 PRI：

```text
src/WinDownloader/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/WinDownloader.pri
```

发布目录不会天然包含所有 WinUI 生成资源，因此 `WinDownloader.csproj` 包含 `CopyWinUIResourcesToPublishDirectory` target，在 `Publish` 后把已生成的 `WinDownloader.pri`、`App.xbf`、`MainWindow.xbf` 和 `Views/**/*.xbf` 复制到 `$(PublishDir)`。

不要为当前项目额外添加脚本生成 `resources.pri`，否则容易和现有 `WinDownloader.pri` 产生重复或路径不一致问题。只有在将来脱离 WinUI MSBuild 管线、改用自定义构建系统，才需要重新设计 MakePri 生成流程。

## 验证

构建：

```powershell
dotnet build .\src\WinDownloader\WinDownloader.csproj -nologo -p:Platform=x64 -v minimal
```

确认输出存在应用 PRI：

```powershell
Get-ChildItem .\src\WinDownloader\bin -Recurse -Filter WinDownloader.pri
```

确认发布输出包含应用 PRI 和 XBF：

```powershell
dotnet publish .\src\WinDownloader\WinDownloader.csproj -c Debug -r win-x64 --self-contained false -nologo -p:Platform=x64 -v minimal
Get-ChildItem .\src\WinDownloader\bin\Debug\net10.0-windows10.0.26100.0\win-x64\publish -Include WinDownloader.pri,*.xbf -Recurse -File
```

需要确认资源键是否进入 PRI 时，可 dump：

```powershell
$makepri = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools\10.0.28000.1839\bin\10.0.28000.0\x64\makepri.exe'
$pri = '.\src\WinDownloader\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\WinDownloader.pri'
$dump = Join-Path $env:TEMP 'WinDownloader.pri.xml'
& $makepri dump /if $pri /of $dump /dt detailed
Select-String -Path $dump -Pattern 'Settings_RestartCard|Nav_Selection|Download_Title'
```

手动 UI 验证：

1. 打开设置页，选择 `English`。
2. 点击“重启应用”。
3. 确认导航、页面标题和设置页静态文本显示英文。
4. 再选择 `中文（简体）` 并重启，确认回到中文。

## 扩展流程

新增静态 UI 字符串：

1. 给 XAML 元素添加稳定 `x:Uid`。
2. 在 `Strings/en-US/Resources.resw` 添加 `{Uid}.{Property}` 英文值。
3. 在 `Strings/zh-CN/Resources.resw` 添加同名中文值。
4. 构建并检查 XAML/资源诊断。
5. 重要页面改动后手动启动应用验证。

新增支持语言：

1. 添加 `Strings/{language}/Resources.resw`。
2. 同步复制所有现有资源键并翻译值。
3. 扩展 `AppSettingsService.NormalizeSupportedLanguage()` 和 `ResolveEffectiveLanguage()`。
4. 扩展 `SettingsViewModel._languageTags`。
5. 在 `SettingsPage.xaml` 添加语言选项和对应资源键。
6. 更新本文档和设置模块文档。

动态字符串本地化：

- 优先新增小型本地化服务封装 Windows App SDK `Microsoft.Windows.ApplicationModel.Resources` API。
- 不要直接使用 UWP 的 `Windows.ApplicationModel.Resources.ResourceLoader` 或 `Windows.Globalization.ApplicationLanguages`。
- 在非 MSIX 场景中，从代码读取字符串时要先验证 PRI 路径和 ResourceMap 名称，必要时用 MakePri dump 确认 URI。
