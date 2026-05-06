# WindowsImageDownloader — 设置服务模块

## 概述

设置模块通过 `IAppSettings` 暴露应用设置，`AppSettingsService` 负责 JSON 持久化、默认值写入和 `INotifyPropertyChanged` 通知。

## 文件清单

| 文件 | 说明 |
|------|------|
| `Interfaces/IAppSettings.cs` | 设置接口 |
| `Services/AppSettingsService.cs` | JSON 设置服务 |
| `ViewModels/SettingsViewModel.cs` | 设置页 VM |
| `Views/Pages/SettingsPage.xaml` / `.cs` | 设置页 UI |

## 接口

```csharp
public interface IAppSettings : INotifyPropertyChanged
{
    int DownloadChunkCount { get; set; }
    int DownloadParallelCount { get; set; }
    int MaxConcurrentDownloads { get; set; }
    string? DownloadDirectory { get; set; }
    string? AppLanguage { get; set; }

    string ResolveEffectiveLanguage();
    void EnsureDefaults();
    void Reset();
}
```

## 默认值

| 设置 | 默认值 | 说明 |
|------|--------|------|
| `DownloadChunkCount` | 32 | Downloader 分块数，范围 1-256 |
| `DownloadParallelCount` | 4 | 单任务并行 HTTP 流数，范围 1-16 |
| `MaxConcurrentDownloads` | 1 | 同时下载任务数，范围 1-16 |
| `DownloadDirectory` | 系统下载文件夹 | 根下载目录 |
| `AppLanguage` | null | 跟随系统 |

## JSON 存储

位置：

```text
%LocalAppData%\WindowsImageDownloader\settings.json
```

实现要点：

- 内存中使用 `Dictionary<string, object?>`。
- 每次写入全量序列化为缩进 JSON。
- `EnsureDefaults()` 只补齐缺失键，不覆盖已有用户设置。
- `Reset()` 恢复默认设置。

## 语言解析

`ResolveEffectiveLanguage()` 优先级：

1. `AppLanguage` 非空且为受支持语言时直接使用。
2. 根据系统 `CultureInfo.CurrentUICulture.Name` 匹配 `zh*` 到 `zh-CN`。
3. 其他语言默认回退到 `en-US`。

当前 UI 本地化只维护 `en-US` 与 `zh-CN` 两套资源。设置页提供“跟随系统”、“English”和“中文（简体）”三个选项；切换语言会立即写入 JSON，但已加载的 WinUI 页面不会实时刷新，需要点击语言卡片下方的“重启应用”卡片或手动重启后生效。详细规则见 [MODULE_LOCALIZATION.md](MODULE_LOCALIZATION.md)。

## 新增设置流程

1. 在 `Interfaces/IAppSettings.cs` 添加属性和 xml-doc。
2. 在 `Services/AppSettingsService.cs` 添加 `Keys`、`Defaults`、属性实现。
3. 在 `SettingsViewModel` 添加绑定属性和回调。
4. 在 `SettingsPage.xaml` 添加控件。
5. 更新本文档默认值和说明。

## 注意事项

- `MaxConcurrentDownloads` 会影响后续准备启动的下载任务；运行时调低不会中断已运行下载。
- `DownloadDirectory` 为空时服务层应回退到默认下载目录。
- `AppLanguage` 仅接受 `en-US` 和 `zh-CN`；其他旧值或无效值会按“跟随系统”处理。
