# WindowsImageDownloader — 本地化模块

## 概述

主应用使用 MRT Core `.resw` 资源，支持 `en-US` / `zh-CN`，切换后重启生效。静态 XAML 使用 `x:Uid`，动态 UI 文本通过 `Helpers/StringRes.cs` 读取资源。

## 文件清单

| 文件 | 说明 |
|------|------|
| `src/WinDownloader/Strings/en-US/Resources.resw` | 英文字符串资源 |
| `src/WinDownloader/Strings/zh-CN/Resources.resw` | 简体中文字符串资源 |
| `src/WinDownloader/App.xaml.cs` | 启动时应用语言覆盖，必须早于 `InitializeComponent()` |
| `src/WinDownloader/Helpers/StringRes.cs` | 动态字符串资源读取 |
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
- 文件组摘要哈希行的 `SHA256: ` 前缀（`RawFileGroupSummary_HashPrefix.Text`），两种语言使用相同文本。
- ViewModel 中通过 `StringRes` 生成的按钮、操作提示、ISO/WIM 阶段文本。

暂不本地化：

- 产品目录中的语言、版本、文件名和 edition 等数据字段。
- 服务和底层工具直接返回的状态、错误消息及诊断输出。
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

- `dotnet build` 将 `Strings/{language}/Resources.resw` 编入输出目录的 `WinDownloader.pri`，无需手动生成 `resources.pri`。
- `CopyWinUIResourcesToPublishDirectory` 在 `Publish` 后复制应用 PRI、`App.xbf`、`MainWindow.xbf` 和 `Views/**/*.xbf` 到 `$(PublishDir)`。
- 缺失资源时先检查输出中的 PRI/XBF，再检查资源键；不要另加一套 MakePri 生成流程。

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

需要检查 PRI 内部资源键时，使用已安装 Windows SDK 的 `makepri dump /if <PRI路径> /of <输出XML> /dt detailed`。

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

动态文本复用 `StringRes`，并在两套资源中添加同名键。使用 Windows App SDK 的资源 API，不使用 UWP `ResourceLoader` 或 `ApplicationLanguages`。
