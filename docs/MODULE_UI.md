# WindowsImageDownloader — UI 层模块

## 概述

UI 层基于 WinUI 3 `NavigationView`，使用 CommunityToolkit.Mvvm 实现 MVVM。页面暴露当前主应用支持的体验：目录筛选、ESD 下载任务管理、完成后 ISO 转换入口和设置。

## 文件清单

| 文件 | 说明 |
|------|------|
| `App.xaml` / `App.xaml.cs` | 应用入口、Host/DI、MainWindow 创建 |
| `MainWindow.xaml` / `.cs` | 主窗口和 NavigationView 导航 |
| `Converters/BoolToVisibilityConverter.cs` | 布尔到可见性转换器 |
| `Views/Pages/SelectionPage.xaml` / `.cs` | 产品目录页 |
| `Views/Pages/DownloadPage.xaml` / `.cs` | 下载任务页 |
| `Views/Pages/SettingsPage.xaml` / `.cs` | 设置页 |
| `Views/Controls/TagControl.xaml` / `.cs` | 标签控件 |
| `Views/Controls/RawFileGroupSummaryControl.xaml` / `.cs` | 产品目录条目和下载任务共享的文件组摘要控件 |
| `Views/Controls/RawFileItemControl.xaml` / `.cs` | 产品目录条目控件 |
| `Views/Controls/DownloadTaskItemControl.xaml` / `.cs` | 下载任务卡片控件 |
| `Views/Controls/WrapPanel.cs` | 简单换行面板 |
| `ViewModels/SelectionViewModel.cs` | 产品目录页 VM |
| `ViewModels/DownloadPageViewModel.cs` | 下载任务页 VM |
| `ViewModels/DownloadTaskItemViewModel.cs` | 单任务项 VM |
| `ViewModels/RawFileItemViewModel.cs` | 目录条目 VM |
| `ViewModels/SettingsViewModel.cs` | 设置页 VM |

## 导航结构

```text
MainWindow
  └─ NavigationView
       ├─ 产品目录      SelectionPage
       ├─ 下载任务      DownloadPage
       └─ 设置          SettingsPage
```

`SelectionPage` 是默认页。

## SelectionViewModel

职责：加载产品目录、维护语言/架构筛选器、按 ESD 文件分组展示，并将用户选择转为 `DownloadTask`。

| 成员 | 说明 |
|------|------|
| `Languages` / `Architectures` | 筛选器选项 |
| `FilteredGroups` | 筛选后的 `RawFileItemViewModel` 集合 |
| `EnsureCatalogLoadedAsync()` | 页面首次进入时加载目录 |
| `ReloadCommand` | 强制刷新目录 |
| `EnqueueDownloadCommand` | 调用 `DownloadTaskOrchestratorService.EnqueueAsync` |

入队时调用：

```csharp
DownloadTask.FromRawFileGroup(group)
```

`RawFileItemControl` 和 `DownloadTaskItemControl` 的顶部摘要都复用 `RawFileGroupSummaryControl`。公共控件绑定 `RawFileGroup`，摘要区显示文件名和 SHA-256；按钮、进度、ISO 转换和错误消息仍保留在各自 item 控件中。

## DownloadPageViewModel

职责：维护下载任务列表和导航徽标计数。

依赖：

- `IDownloadTaskOrchestratorService`
- `IEsdToIsoOrchestratorService`
- `IDownloadTaskPathService`

它订阅：

| 事件 | 行为 |
|------|------|
| `TaskAdded` | UI 线程插入 `DownloadTaskItemViewModel` |
| `TaskRemoved` | UI 线程移除并 dispose 任务 VM |
| `TaskChanged` | 将下载快照转发给对应 task item |
| `ConversionChanged` | 将 ISO 转换快照转发给对应 task item |
| `ActiveTaskCountChanged` | 聚合下载和 ISO active 计数，更新导航徽标 |

`PendingTaskCount` 当前显示两个编排服务的 active worker 总数：正在下载、校验或转换 ISO 的 worker 会计入；仅排队等待下载槽或转换槽的任务不会计入。

## DownloadTaskItemViewModel

职责：把 `DownloadTaskSnapshot` 和 `IsoConversionTaskSnapshot` 转成 XAML 绑定属性，并暴露任务操作命令。下载快照和 ISO 转换快照分别合并到 ViewModel，再通过 `DispatcherQueue.TryEnqueue` 应用到 UI 线程。

| 状态属性 | 说明 |
|----------|------|
| `IsQueued` | 排队或暂停 |
| `IsDownloading` | 下载中 |
| `IsVerifying` | SHA-256 校验中 |
| `IsCompleted` | 已完成 |
| `IsFailed` | 失败 |
| `ShowDownloadProgress` | 显示进度区 |
| `ShowIsoProgress` | 显示 ISO 转换主/子进度区 |
| `ShowActionBar` | 完成后显示操作区 |
| `IsIsoConversionBusy` | ISO 转换已排队或正在运行 |
| `IsoMainProgress` / `IsoSubProgress` | ISO 主进度和当前子步骤进度 |
| `IsoMainStatusText` / `IsoSubStatusText` | ISO 主阶段文本和子阶段文本 |

| 命令 | 说明 |
|------|------|
| `PauseCommand` | 暂停正在下载的任务 |
| `ResumeCommand` | 恢复 queued 任务 |
| `CancelCommand` | 取消、移除 queued/downloading/verifying/failed 任务 |
| `ConvertToIsoCommand` | 完成下载后转换 ISO；若 ISO 已存在则打开 ISO 所在目录 |
| `DeleteCommand` | 删除 completed 且没有 ISO 转换中的任务和 ESD 文件 |
| `OpenDirectoryCommand` | 通过 `IDownloadTaskPathService` 打开输出目录 |

`DownloadTaskItemControl.xaml` 在下载进度区之后显示 ISO 进度组：

```text
IsoMainStatusText
IsoMainProgress
IsoSubStatusText
IsoSubProgress / IsIsoSubProgressIndeterminate
```

子进度优先使用 `EsdToIsoTaskSnapshot.IsoProgress.Percent` 或 `WimProgress.Percent`；当底层阶段没有百分比时使用不确定进度条。操作区包含删除、转换 ISO/打开 ISO 目录、打开 ESD 目录三个入口。

## SettingsViewModel

| 属性 | 说明 |
|------|------|
| `DownloadDirectory` | 下载目录 |
| `DownloadChunkCount` | Downloader 分块数 |
| `DownloadParallelCount` | 单任务并行 HTTP 流数 |
| `MaxConcurrentDownloads` | 同时下载的任务数 |
| `SelectedLanguageIndex` | UI 语言选择 |
| `ResetCommand` | 恢复默认设置 |

## 本地化

静态 XAML 文本使用 `x:Uid` 绑定 `.resw` 资源；详细资源范围、命名约定、PRI 行为和语言切换规则见 [MODULE_LOCALIZATION.md](MODULE_LOCALIZATION.md)。

## 注意事项

- `DispatcherQueue.GetForCurrentThread()` 必须在 UI 线程调用。
- `DownloadTaskItemViewModel` 会合并高频下载和 ISO 快照，避免每个进度事件都立即刷新 UI。
- 页面通过 `App.GetService<T>()` 获取 ViewModel。
- ISO 转换中不允许删除任务；`CanDelete` 会被 `IsIsoConversionBusy` 阻止。
- 不在 UI 层拼路径；ESD、ISO、`.staging` 路径都来自 `IDownloadTaskPathService`。
