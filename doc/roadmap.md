# WindowsImageDownloader WinUI 3 App 开发路线图

## 📋 概述

本项目基于两个 Shell 脚本（`download-windows-esd.txt` 和 `windows-esd-to-iso.txt`）的功能，构建一个功能完整的 **WinUI 3** 桌面应用程序，用于**查询、下载 Windows ESD 映像**，并可选择在下载完成后**自动转换为 WIM 格式或 ISO 安装介质**（支持自由组合输出）。

---

## 预备知识：WIM、ESD、ISO 的关系

### 这三种格式分别是什么？

| 格式 | 全称 | 本质 | 典型用途 |
|-----|------|------|---------|
| **WIM** | Windows Imaging Format | 微软自家的**磁盘映像文件格式**。特点是：基于文件（非扇区级）、支持在一个文件里放多个"映像索引"（例如索引1=家庭版、索引2=专业版）、支持压缩、可设启动标志 | Windows 安装包（`install.wim`）、企业批量部署、系统备份 |
| **ESD** | Electronic Software Download | **WIM 的高压缩变种**。微软在 Windows 10/11 时代推出的格式，压缩率更高（使用 LZMS 算法），所以文件更小。**但代价是：不能直接挂载，不能直接启动** | Windows Update 在线推送、微软官方下载中心提供的 Windows 安装文件 |
| **ISO** | ISO 9660 光盘镜像 | 通用的**光盘镜像格式**。内容是完整的、可直接启动的光盘目录结构。用户可以写入 U 盘或者挂载到虚拟机直接安装系统 | 安装盘、启动盘、虚拟机挂载 |

### 用生活中的例子理解

```
   ESD = 快递包裹（压缩打包，方便运输，但不能直接使用）
   WIM = 零件箱（拆开包裹后，里面的零件可以进一步组装）
   ISO = 组装好的成品（可以直接用，插上 U 盘就能装系统）
```

### 具体到本 App 的转换流程

参考 `windows-esd-to-iso.txt` 脚本，ESD → ISO 的实际步骤如下：

```
从微软下载的 ESD 文件（高压缩，不能直接用）
  │
  ├─ 映像①: Windows 安装文件本身
  │   → 直接解压到临时目录（wimapply）
  │
  ├─ 映像②: Windows 恢复环境（WinRE）
  │   → 导出为 boot.wim，LZX 压缩，32K 块大小
  │
  ├─ 映像③: Windows 启动环境（Boot）
  │   → 导出追加到 boot.wim，设可启动标志
  │
  └─ 映像④~Ⓝ: 实际 Windows 系统（家庭版/专业版等）
      → 全部导出合并到单个文件，压缩算法取决于输出格式
          ├─ install.esd — LZMS 重压缩，128K 块（体积最小）
          ├─ install.wim — LZX 压缩，32K 块（可挂载编辑）
          └─ install.wim → 打包为 ISO（可启动安装盘）
  │
  └── 最终：临时目录结构
       ├─ bootmgr / bootmgr.efi      ← 启动管理器
       ├─ boot/                        ← BCD 启动配置
       ├─ sources/
       │   ├─ boot.wim                ← 启动+恢复环境
       │   └─ install.esd/wim         ← 实际的 Windows 系统
       ├─ efi/microsoft/boot/efisys.bin ← EFI 启动扇区
       └─ ...
       ──────────────────────────▶ 打包为 ISO（可启动光盘镜像）
```

### 一句话总结

> **ESD** 是微软给你的"快递包"——体积小适合下载，但不能直接用；
> **WIM** 是中间的"零件"——拆开后可以加工重组；
> **ISO** 是最终的"成品光盘"——拿到手写进 U 盘就能装系统。

本 App 做的就是：**把微软给的快递包（ESD）拆开重组，根据你的需要输出 WIM 零件箱（可挂载编辑）或打包成安装光盘（ISO）**，省去手动敲命令的麻烦。

WIM 格式特别适合**企业批量部署（MDT/SCCM）** 和 **DIY 定制系统**的场景——它支持直接挂载编辑，而 ESD 和 ISO 都不行。

---

## 一、功能分析

### 1.1 download-windows-esd.txt 功能拆解

| 功能模块 | 说明 | Shell 实现方式 | WinUI 3 实现方向 |
|---------|------|---------------|-----------------|
| **获取产品目录** | 从 Microsoft Update 服务获取 `products.xml` | `curl` + Windows Update REST API | `HttpClient` + Windows Update 服务 API |
| **缓存管理** | 缓存 `products.xml`，按天检查更新 | 文件系统 + `find -mmin` | SQLite 缓存策略 |
| **查询语言** | 列出所有可用语言 | `xpath` 解析 XML | `System.Xml.Linq` 解析 XML |
| **查询版本** | 列出某语言下的所有版本 | `xpath` 解析 XML | `System.Xml.Linq` 解析 XML |
| **查询架构** | 列出某语言+版本下的所有架构 | `xpath` 解析 XML | `System.Xml.Linq` 解析 XML |
| **下载 ESD** | 下载 ESD 文件 + SHA-256 校验 | `curl` + `shasum` | `HttpClient` 断点续传 + SHA256 |
| **获取 URL** | 获取文件直接下载链接 | `xpath` 提取 | LINQ to XML 提取 |
| **获取 SHA-256** | 获取文件的 SHA-256 哈希 | `xpath` 提取 | LINQ to XML 提取 |

### 1.2 windows-esd-to-iso.txt 功能拆解

| 功能模块 | 说明 | Shell 实现方式 | WinUI 3 实现方向 |
|---------|------|---------------|-----------------|
| **解析 ESD 映像信息** | 获取 ESD 中的映像数量 | `wiminfo` | **ManagedWimLib + wimlib** |
| **导出映像 1** | 导出到临时目录作为安装源 | `wimapply` | **ManagedWimLib + wimlib** |
| **导出映像 2 → boot.wim** | 压缩导出到 boot.wim | `wimexport --compress=LZX` | **ManagedWimLib + wimlib** |
| **导出映像 3 → boot.wim** | 导出为可启动映像 | `wimexport --compress=LZX --boot` | **ManagedWimLib + wimlib** |
| **导出映像 4+ → install.esd/wim** | 后续映像重压缩输出 | `wimexport --compress=LZMS` | **ManagedWimLib + wimlib**（支持 LZX/WIM 输出） |
| **创建 ISO** | 将目录结构制作成 ISO | `hdiutil makehybrid` | **DiscUtils** NuGet 包 |

---

## 二、技术选型（最终确定）

### 2.1 WIM/ESD 处理：wimlib

使用 **ManagedWimLib** NuGet 包作为托管封装，并通过 `Wim.GlobalInit(customPath, InitFlags.None)` 显式加载随应用发布的 `libwim-15.dll`。

- 应用代码不维护自研 P/Invoke、SafeHandle 或回调 marshaling；这些底层细节由 ManagedWimLib 处理
- **单一 native DLL 文件**（约 2MB），无需安装，随应用发布即可
- 完全覆盖 Shell 脚本需求：`wiminfo`、`wimapply`、`wimexport` 及其 `--compress`、`--boot`、`--chunk-size`、`--recompress` 参数
- **ESD ↔ WIM 互转只是改一个压缩参数的事**——wimlib 原生支持
  - ESD = LZMS 压缩 + 固态归档
  - WIM = LZX 压缩（可挂载编辑）
- 下载地址：[wimlib.net](https://wimlib.net/) — 取 Windows 版二进制包中的 `libwim-15.dll`
- 许可证注意：ManagedWimLib 与 wimlib 为 LGPL-3.0-or-later，发布前需要在 release 文档中保留许可证说明

### 2.2 ISO 创建：DiscUtils

使用 **DiscUtils** NuGet 包，纯托管代码，不依赖任何外部工具。

- 支持 ISO 9660 Level 3（>4GB 文件）
- 支持 UDF 桥接
- 支持 El Torito EFI 启动（指向 `efi/microsoft/boot/efisys.bin`）
- 支持自定义卷标
- **不需要安装 Windows ADK 或任何其他工具**

```xml
<PackageReference Include="Discutils" Version="0.*" />
```

### 2.3 依赖总结

```
┌──────────────────────────────────────────────────┐
│              WindowsImageDownloader                │
├──────────────────────────────────────────────────┤
│  NuGet 依赖（编译时）：                              │
│  • CommunityToolkit.Mvvm     — MVVM 框架          │
│  • DiscUtils                 — ISO 创建            │
│  • ManagedWimLib             — wimlib 托管封装      │
│  • Microsoft.Data.Sqlite     — 缓存数据库          │
│                                                    │
│  运行时附带文件：                                     │
│  • libwim-15.dll             — WIM/ESD 处理 (~2MB)  │
│                                                    │
│  用户无需安装任何额外组件！                            │
└──────────────────────────────────────────────────┘
```

### 2.4 缓存/配置方案

| 技术 | 用途 |
|-----|------|
| **SQLite** (`Microsoft.Data.Sqlite`) | 缓存 products.xml 解析结果、下载任务、转换任务状态 |
| **JSON 文件** | 存储用户配置（下载目录、代理、默认格式等） |

---

## 三、用户界面与工作流设计

### 3.1 核心交互流程

核心设计思想：**从选产品到拿到安装文件是一条龙服务，用户只需点几次鼠标。**

```
用户流程：
  ① 打开 App → ② 浏览目录（选语言 → 选架构）
               ③ 选择输出格式（复选框自由组合）：
                  ☑ ESD 文件（原始下载）
                  ☐ WIM 文件（LZX 重压缩，可挂载编辑）
                  ☐ ISO 镜像（可启动安装盘）
               ④ 点击"添加下载任务"
               ⑤ 在下载管理页查看进度
               ⑥ 下载完成后自动开始转换（勾选的格式都会生成）
               ⑦ 完成！
```

### 3.2 页面设计

```
┌─────────────────────────────────────────────────────┐
│  NavigationView (左侧导航)                           │
│  ┌──────────────────────────────────────────┐       │
│  │ 🖼️ 产品目录                               │       │
│  │ ⬇️ 下载任务                               │       │
│  │ ⚙️ 设置                                  │       │
│  └──────────────────────────────────────────┘       │
│                                                     │
│  ┌──────────────────────────────────────────┐       │
│  │  内容区域 (Frame)                         │       │
│  │                                          │       │
│  │  ┌─ CatalogPage ──────────────────────┐  │       │
│  │  │ [语言 ▼] [架构 ▼]              ↓  │  │       │
│  │  │   二级联动选择器，筛选可用映像        │  │       │
│  │  │                                    │  │       │
│  │  │  文件信息：                         │  │       │
│  │  │  文件名: Windows11_xxx.esd         │  │       │
│  │  │  大小: 5.2 GB                      │  │       │
│  │  │  SHA-256: a1b2c3...               │  │       │
│  │  │                                    │  │       │
│  │  │  输出格式（可多选）：                │  │       │
│  │  │  ☑ 📦 ESD 文件（原始下载）          │  │       │
│  │  │  ☐ 📁 WIM 文件（LZX，可挂载编辑）    │  │       │
│  │  │  ☑ 💿 ISO 镜像（可启动安装盘）       │  │       │
│  │  │                                    │  │       │
│  │  │         [📥 添加下载任务]          │  │       │
│  │  └────────────────────────────────────┘  │       │
│  │                                          │       │
│  │  ┌─ DownloadPage ─────────────────────┐  │       │
│  │  │  ┌─────────────────────────────────┐│  │       │
│  │  │  │ Windows 11 Pro zh-cn amd64     ││  │       │
│  │  │  │ ████████████████░░░░ 75%       ││  │       │
│  │  │  │ 下载中... 3.9/5.2 GB  12MB/s   ││  │       │
│  │  │  │ [暂停] [取消]                   ││  │       │
│  │  │  ├─────────────────────────────────┤│  │       │
│  │  │  │ Windows 11 Home zh-cn amd64    ││  │       │
│  │  │  │ ████████████████████████ 100%   ││  │       │
│  │  │  │ 📦📁💿 下载完成 → 转换中...    ││  │       │
│  │  │  │ ✅ ESD 已完成                   ││  │       │
│  │  │  │ ⏳ WIM 转换中 ██████░░ 60%     ││  │       │
│  │  │  │ ⏳ ISO 转换中 ████░░░░ 40%     ││  │       │
│  │  │  │ [打开文件夹]                    ││  │       │
│  │  │  └─────────────────────────────────┘│  │       │
│  │  └────────────────────────────────────┘  │       │
│  └──────────────────────────────────────────┘       │
└─────────────────────────────────────────────────────┘
```

### 3.3 任务状态机

```
                  [新建任务]
                      │
                      ▼
           ┌──────────────────┐
           │   等待下载队列     │
           └────────┬─────────┘
                    │
                    ▼
           ┌──────────────────┐
           │    正在下载       │
           │  (断点续传+进度)  │
           └────────┬─────────┘
                    │
          ┌─────────┴──────────┐
          ▼                    ▼
   ┌──────────────┐   ┌───────────────┐
   │ 下载完成      │   │  下载失败      │
   │ SHA-256 校验  │   │  (网络/校验)   │
   └──────┬───────┘   └───────┬───────┘
          │                   │
    ┌─────┴───────────────────┘ 重试/取消
    │
    ├─ [仅 ESD] ────────────────→ 完成！
    │
    ├─ [含 WIM] ─── ESD→WIM (LZX重压缩) ──→ 完成！
    │
    ├─ [含 ISO] ─── 解压+重组 ──→ 打包ISO ──→ 完成！
    │
    └─ [ESD+WIM+ISO] ──→ ESD保留 ──→ 转WIM ──→ 转ISO ──→ 全部完成！
```

---

## 四、项目结构

```
WindowsImageDownloader/
├── App.xaml / App.xaml.cs            # 应用入口 ✅
├── MainWindow.xaml / .cs             # 主窗口 ✅
├── app.manifest                      # 应用清单 ✅
├── WindowsImageDownloader.csproj     # 项目文件 ✅
│
├── Models/                           # 数据模型 ✅
│   ├── RawFile.cs                    # ✅ products.xml 原始文件模型
│   ├── RawFileGroup.cs               # ✅ 文件分组模型
│   ├── CatalogOption.cs              # ✅ 筛选选项模型
│   ├── TagType.cs                    # ✅ 标签颜色枚举
│   ├── WimCompressionKind.cs         # ✅ 压缩类型枚举
│   ├── WimExportRequest.cs           # ✅ WIM 导出请求
│   ├── WimImageInfo.cs               # ✅ WIM 映像信息
│   ├── WimLibraryInfo.cs             # ✅ WIM 库信息
│   └── WimOperationProgress.cs       # ✅ WIM 操作进度
│
├── Services/                         # 核心服务层
│   ├── IUpdateCatalogService.cs      # ✅ 更新目录服务接口
│   ├── UpdateCatalogService.cs       # ✅ 已实现：REST API → CAB → XML → RawFile
│   ├── IWimProcessingService.cs      # ✅ WIM 处理服务接口
│   ├── WimProcessingService.cs       # ✅ 已实现：ManagedWimLib 封装
│   ├── ICacheService.cs              # ❌ 待实现：SQLite 缓存
│   ├── CacheService.cs               # ❌
│   ├── IDownloadService.cs           # ❌ 待实现：断点续传下载
│   ├── DownloadService.cs            # ❌
│   ├── IIsoCreationService.cs        # ❌ 待实现：DiscUtils ISO 创建
│   ├── IsoCreationService.cs         # ❌
│   ├── ITaskOrchestratorService.cs   # ❌ 待实现：下载→转换流水线
│   └── TaskOrchestratorService.cs    # ❌
│
├── ViewModels/                       # ViewModel 层
│   ├── SelectionViewModel.cs         # ✅ 已实现：二级联动筛选
│   ├── DownloadListViewModel.cs      # ❌ 待实现
│   └── SettingsViewModel.cs          # ❌ 待实现
│
├── Views/                            # UI 页面
│   ├── Pages/
│   │   ├── SelectionPage.xaml / .cs  # ✅ 已实现：产品目录浏览
│   │   ├── DownloadListPage          # ❌ 待实现：下载任务列表
│   │   └── SettingsPage              # ❌ 待实现：设置页
│   └── Controls/
│       ├── RawFileItemControl        # ✅ 已实现：文件卡片
│       ├── TagControl                # ✅ 已实现：标签控件
│       ├── WrapPanel.cs              # ✅ 已实现：自动换行面板
│       ├── TaskItemControl           # ❌ 待实现：任务进度卡片
│       ├── FormatSelectorControl     # ❌ 待实现：输出格式选择
│       └── LogOutputControl          # ❌ 待实现：日志输出
│
├── Helpers/                          # ❌ 待创建
│   ├── XmlParser.cs                  # ❌ (功能已内联在 UpdateCatalogService)
│   ├── Sha256Helper.cs               # ❌ (功能已内联在 UpdateCatalogService)
│   ├── FileSizeFormatter.cs          # ❌ (功能已内联在 RawFile)
│
├── Resources/                        # ❌ 待创建
│   ├── libwim-15.dll                 # ❌ 待下载
│   └── Strings/                      # ❌ 待创建
│
└── Assets/                           # ✅ 已存在
    └── ...
```

---

## 五、开发路线图（按阶段）

### 阶段 1：项目初始化与环境搭建 ⏱ 1-2 天 ✅ **已完成**

- [x] 1.1 确认开发环境（WinUI 3 + .NET 10)
- [x] 1.2 项目结构搭建（创建文件夹和类文件骨架、安装 NuGet 包依赖）
- [x] 1.3 基础 MVVM 框架（CommunityToolkit.Mvvm、DI、NavigationView 导航）
- [x] NuGet 依赖已安装：CommunityToolkit.Mvvm, DiscUtils, ManagedWimLib, Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection

---

### 阶段 2：产品目录浏览功能 ⏱ 3-4 天 ✅ **已完成**

- [x] 2.1 `UpdateCatalogService` 实现
  - Windows Update REST API：POST `https://fe3.delivery.mp.microsoft.com/UpdateMetadataService/updates/search/v1/bydeviceinfo`
  - 请求体：`{"Products": "PN=Windows.Products.Cab.amd64&V=26100.0.0.0", "DeviceAttributes": "DUScan=1;OSVersion=10.0.026100.1"}`
  - 响应解析：从 JSON 中提取 `FileLocations[0].Url` 和 `FileLocations[0].Digest`
- [x] 2.2 下载 products.cab + SHA-256 校验 + expand.exe 解压
- [x] 2.3 Products.xml 解析器（LINQ to XML 解析 RawFile 列表）
- [x] 2.4 SelectionViewModel + SelectionPage UI（语言/架构二级联动、文件卡片列表）
- [x] 2.5 自定义控件：TagControl（6色标签）、WrapPanel（自动换行）、RawFileItemControl（文件卡片含下载按钮）

---

### 阶段 3：下载 + 自动转换一站式流水线 ⏱ 待实现 ← **从这里开始**

这是最核心的阶段——将 Shell 脚本中的下载和转换整合为一条无缝流水线。

#### 3.1 下载引擎

- [ ] 3.1.1 `DownloadService` 核心实现
  - 基于 `HttpClient` 的断点续传（`Range` 请求头）
  - 对应 Shell 脚本的 `--continue-at -`
  - 进度报告：当前速度（MB/s）、已下载/总大小、剩余时间（ETA）
- [ ] 3.1.2 下载队列管理
  - 支持同时下载多个文件（可配置并行数）
  - 暂停/恢复/取消下载任务
  - 断电续传：保存下载状态到 SQLite
- [ ] 3.1.3 SHA-256 校验
  - 下载完成后使用 `System.Security.Cryptography.SHA256` 计算哈希
  - 对比 products.xml 中的预期值
  - 校验失败提示并保留/删除文件选项

#### 3.2 WIM 处理引擎（ManagedWimLib + wimlib）

- [x] 3.2.1 ManagedWimLib 集成 ✅
- [ ] 3.2.2 ESD → WIM / ESD → ISO 完整转换流程集成
  - 临时目录管理（创建/清理临时工作目录）
  - 映像解压到目录（对应 `wimapply`）
  - boot.wim 创建（LZX 压缩 + 可启动标志）
  - install.wim/esd 导出（可配置 LZMS/LZX 压缩）
- [ ] 3.2.3 准备 libwim-15.dll 运行时文件

#### 3.3 ISO 创建引擎（DiscUtils）

- [ ] 3.3.1 `IsoCreationService` 实现
  - 使用 `DiscUtils.Iso9660.CDBuilder` 创建 ISO
  - **无需任何外部工具，纯 .NET 代码**
- [ ] 3.3.2 实现 ISO 创建核心方法
  - `CreateBootableIso(sourceDir, outputPath, volumeName)` → 创建 UEFI 可启动 ISO
  - 设置 El Torito EFI 启动入口（指向 `efi/microsoft/boot/efisys.bin`）
  - 设置 ISO 9660 卷标 + UDF 桥接卷标

#### 3.4 任务编排引擎

- [ ] 3.4.1 `TaskOrchestratorService` 实现
  - 负责任务的完整生命周期：等待下载 → 下载中 → SHA-256校验 → (如需WIM/ISO) → 转换中 → 完成
  - 根据输出格式决定后续步骤：
    - 仅 ESD → 下载完成 + 校验通过 → 完成
    - ESD + WIM → 下载完成 + 校验通过 → 用 LZX 重压缩为 WIM → 完成
    - ESD + ISO → 下载完成 + 校验通过 → 用 DiscUtils 生成 ISO → 完成
    - ESD + WIM + ISO → 三步走：保留 ESD → 转 WIM → 用 WIM 制作 ISO → 全部完成
- [ ] 3.4.2 下载任务模型（DownloadTask、OutputFormat 枚举等）

#### 3.5 任务列表 UI

- [ ] 3.5.1 `DownloadListPage` UI 实现
  - 每个任务一张卡片，显示进度、速度/ETA、状态文字、操作按钮
  - 多格式转换时显示子任务进度
  - 排序、清除已完成/全部
- [ ] 3.5.2 导航菜单添加"下载任务"入口

---

### 阶段 4：用户界面完善 ⏱ 2-3 天

- [ ] 4.1 主题与样式（浅色/深色主题、自定义控件样式）
- [ ] 4.2 设置页面
  - 下载目录设置
  - 临时目录设置
  - 同时下载任务数限制
  - 默认输出格式（ESD / ESD+WIM / ESD+ISO / 全选）
  - 代理设置
  - 缓存清理按钮
  - 语言选择（中文/English）
- [ ] 4.3 任务历史（已完成任务列表 + 清除历史功能）
- [ ] 4.4 错误处理与用户体验（全局异常、网络重试、操作确认对话框、通知）
- [ ] 4.5 本地化（中英文界面，`.resw` 资源文件）

---

### 阶段 5：测试与发布 ⏱ 2-3 天

- [ ] 5.1 单元测试（Services 层、XML 解析、SHA-256 校验、DiscUtils ISO 创建）
- [ ] 5.2 集成测试（完整工作流：选产品 → 下载 → 自动转换 → 输出三种格式）
- [ ] 5.3 Unpackaged 发布（确认 libwim-15.dll 输出、应用签名）
- [ ] 5.4 GitHub Releases 发布

---

## 六、当前完成状态

### ✅ 已实现（可直接运行的代码）

| 组件 | 状态 | 说明 |
|------|------|------|
| `UpdateCatalogService` | ✅ 完整实现 | Windows Update REST API → CAB下载 → SHA-256校验 → expand.exe解压 → XML解析 |
| `WimProcessingService` | ✅ 完整实现 | ManagedWimLib 封装：GetImages / ExtractImage / ExportImage + 进度回调 |
| `SelectionViewModel` | ✅ 完整实现 | 语言→架构二级联动筛选，加载/错误/空状态处理 |
| `SelectionPage` UI | ✅ 完整实现 | 产品目录浏览页：ComboBox筛选 + ListView卡片列表 |
| `RawFileItemControl` | ✅ 完整实现 | 文件卡片：标题/Tag/Editions列表/下载按钮 |
| `TagControl` | ✅ 完整实现 | 6色标签控件（Primary/Success/Warning/Danger/Info/Default） |
| `WrapPanel` | ✅ 完整实现 | 自动换行面板（HorizontalSpacing/VerticalSpacing） |
| 全部 Model | ✅ 已创建 | 10个模型/枚举文件 |

### ❌ 尚未实现（接下来的开发重点）

| 组件 | 优先级 | 说明 |
|------|--------|------|
| 下载服务 | 🔴 P0 | `IDownloadService` / `DownloadService` — 断点续传下载 |
| ISO 创建服务 | 🔴 P0 | `IIsoCreationService` / `IsoCreationService` — DiscUtils 封装 |
| 任务编排服务 | 🔴 P0 | `ITaskOrchestratorService` / `TaskOrchestratorService` — 完整流水线 |
| 下载任务模型 | 🔴 P0 | `DownloadTask`, `OutputFormat` flags, `TaskState` 枚举 |
| 下载列表页 | 🔴 P0 | `DownloadListPage` + `DownloadListViewModel` |
| 任务卡片控件 | 🟡 P1 | `TaskItemControl` — 进度/状态/操作 |
| 格式选择控件 | 🟡 P1 | `FormatSelectorControl` — ESD/WIM/ISO 多选 |
| SQLite 缓存服务 | 🟡 P1 | `CacheService` — 结构化缓存 |
| 设置页面 | 🟢 P2 | `SettingsPage` + `SettingsViewModel` |
| 导航整合 | 🟢 P2 | 添加下载任务/设置菜单项到 NavigationView |
| libwim-15.dll | 🟢 P2 | 下载 wimlib 二进制包，提取 DLL |
| 本地化 | 🟢 P2 | .resw 中英文资源文件 |

---

## 七、关键实现细节

### 7.1 Windows Update REST API 调用

```csharp
var requestBody = new
{
    Products = "PN=Windows.Products.Cab.amd64&V=26100.0.0.0",
    DeviceAttributes = "DUScan=1;OSVersion=10.0.026100.1"
};

var response = await httpClient.PostAsJsonAsync(
    "https://fe3.delivery.mp.microsoft.com/UpdateMetadataService/updates/search/v1/bydeviceinfo",
    requestBody);

var result = await response.Content.ReadFromJsonAsync<SearchResult[]>();
var cabUrl = result[0].FileLocations[0].Url;
var expectedDigest = result[0].FileLocations[0].Digest; // Base64 SHA-256
```

### 7.2 断点续传下载

```csharp
public async Task DownloadWithResumeAsync(
    string url, string filePath,
    IProgress<DownloadProgress> progress,
    CancellationToken ct)
{
    var fileInfo = new FileInfo(filePath);
    long existingBytes = fileInfo.Exists ? fileInfo.Length : 0;

    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    if (existingBytes > 0)
        request.Headers.Range = new RangeHeaderValue(existingBytes, null);

    using var response = await httpClient.SendAsync(
        request, HttpCompletionOption.ResponseHeadersRead, ct);

    using var responseStream = await response.Content.ReadAsStreamAsync(ct);
    using var fileStream = new FileStream(filePath,
        existingBytes > 0 ? FileMode.Append : FileMode.Create);

    var totalBytes = (response.Content.Headers.ContentRange?.Length
                     ?? response.Content.Headers.ContentLength) ?? -1;
    var buffer = new byte[81920];
    long bytesWritten = existingBytes;
    int bytesRead;
    var sw = Stopwatch.StartNew();

    while ((bytesRead = await responseStream.ReadAsync(buffer, ct)) > 0)
    {
        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        bytesWritten += bytesRead;
        progress.Report(new DownloadProgress(bytesWritten, totalBytes, sw.Elapsed));
    }
}
```

### 7.3 products.xml 解析（当前已实现）

详见 `UpdateCatalogService.ParseProductsXml()` — 使用 LINQ to XML 解析 `LanguageCode`, `Language`, `Architecture`, `Edition_Loc`, `Edition`, `FileName`, `FilePath`, `Sha256`, `Size`, `IsRetailOnly`。

### 7.4 ESD → WIM 转换（wimlib LZX 重压缩）

WIM 输出本质上就是把 ESD 的 LZMS 压缩解压后用 LZX 重新打包——wimlib 一个 `ExportImage` 调用即可完成。

```csharp
public async Task ConvertEsdToWimAsync(
    string esdFilePath,
    string outputWimPath,
    IProgress<string> progress,
    CancellationToken ct)
{
    var images = await wimProcessingService.GetImagesAsync(esdFilePath, ct);

    // 映像 1 通常是安装文件元数据，跳过
    // 映像 2,3 = boot.wim（启动环境），ESD→WIM 时自动处理
    // 映像 4+ = 实际系统版本，LZMS→LZX 重压缩

    string installWimPath = outputWimPath;
    for (int i = 2; i <= images.Count; i++)
    {
        ct.ThrowIfCancellationRequested();
        progress.Report($"导出映像 {i}/{images.Count}...");

        bool isBootable = (i == 2 || i == 3);  // 前两个是启动映像
        bool isFirst = (i == 2);
        await wimProcessingService.ExportImageAsync(
          new WimExportRequest(
            esdFilePath,
            installWimPath,
            i,
            images[i - 1].Name,
            images[i - 1].Description,
            WimCompressionKind.LZX,
            MarkBootable: isBootable),
          cancellationToken: ct);
    }

    progress.Report("WIM 转换完成！");
}
```

### 7.5 ESD → ISO 转换核心逻辑（wimlib + DiscUtils）

```csharp
public async Task ConvertEsdToIsoAsync(
    string esdFilePath,
    string outputDirectory,
    bool keepEsdFile,
    IProgress<ConvertProgress> progress,
    CancellationToken ct)
{
    var tempDir = Path.Combine(Path.GetTempPath(), $"esd2iso_{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);

    try
    {
        var images = await wimProcessingService.GetImagesAsync(esdFilePath, ct);

        // 导出映像 1 → 临时目录
        progress.Report(new("解压安装映像...", 0, images.Count + 2));
        await wimProcessingService.ExtractImageAsync(esdFilePath, 1, tempDir, cancellationToken: ct);

        // 导出映像 2 → boot.wim (LZX, 32K)
        var bootWim = Path.Combine(tempDir, "sources", "boot.wim");
        await wimProcessingService.ExportImageAsync(
          new WimExportRequest(esdFilePath, bootWim, 2, images[1].Name, images[1].Description),
          cancellationToken: ct);

        // 导出映像 3 → boot.wim (LZX, 32K, 可启动)
        progress.Report(new("导出引导映像...", 1, images.Count + 2));
        await wimProcessingService.ExportImageAsync(
          new WimExportRequest(esdFilePath, bootWim, 3, images[2].Name, images[2].Description, MarkBootable: true),
          cancellationToken: ct);

        // 导出映像 4+ → install.esd (LZMS, 128K)
        var installEsd = Path.Combine(tempDir, "sources", "install.esd");
        for (int i = 4; i <= images.Count; i++)
        {
          progress.Report(new($"导出安装映像 {i}/{images.Count}...", i - 1, images.Count + 2));
          await wimProcessingService.ExportImageAsync(
            new WimExportRequest(esdFilePath, installEsd, i, images[i - 1].Name, images[i - 1].Description, WimCompressionKind.LZMS),
            cancellationToken: ct);
        }

        // 使用 DiscUtils 创建 ISO
        progress.Report(new("正在生成 ISO 镜像...", images.Count + 1, images.Count + 2));
        var outputIso = Path.Combine(outputDirectory,
            $"{Path.GetFileNameWithoutExtension(esdFilePath)}.iso");

        using (var isoStream = File.Create(outputIso))
        {
            var builder = new CDBuilder()
            {
                UseJoliet = true,
                VolumeIdentifier = "ESD_ISO"
            };
            AddDirectoryToIso(builder, tempDir, tempDir);
            builder.Build(isoStream);
        }

        if (!keepEsdFile && File.Exists(esdFilePath))
            File.Delete(esdFilePath);

        progress.Report(new("完成!", images.Count + 2, images.Count + 2));
    }
    finally
    {
        if (!keepTempFiles)
            Directory.Delete(tempDir, recursive: true);
    }
}
```

### 7.6 任务模型（支持多格式输出）

```csharp
[Flags]
public enum OutputFormat
{
    Esd = 1 << 0,     // 001 = 保留原始 ESD
    Wim = 1 << 1,     // 010 = 转 WIM（LZX 重压缩）
    Iso = 1 << 2      // 100 = 转 ISO（可启动安装盘）
}

public class DownloadTask : ObservableObject
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private OutputFormat _format;       // Esd | Esd|Wim | Esd|Iso | Esd|Wim|Iso
    [ObservableProperty] private TaskState _state;            // Waiting / Downloading / Verifying / Converting / Completed / Failed
    [ObservableProperty] private double _progress;            // 0~1（总体进度）
    [ObservableProperty] private string _statusText;

    [ObservableProperty] private string _downloadUrl;
    [ObservableProperty] private string _expectedSha256;
    [ObservableProperty] private string _esdFilePath;
    [ObservableProperty] private string _wimFilePath;         // 可选输出
    [ObservableProperty] private string _isoFilePath;         // 可选输出
    [ObservableProperty] private string _errorMessage;

    // 各子任务进度（用于多格式同时转换时细分显示）
    [ObservableProperty] private double _wimProgress;         // WIM 转换进度
    [ObservableProperty] private double _isoProgress;         // ISO 转换进度
}

public enum TaskState { Waiting, Downloading, Verifying, Converting, Completed, Failed }
```

---

## 八、风险与挑战

| 风险 | 影响 | 缓解策略 |
|-----|------|---------|
| Windows Update API 变动 | 无法获取产品目录 | 监控 API 变化，及时适配；提供手动导入 products.xml 功能 |
| ManagedWimLib / wimlib 版本兼容 | 转换流程受阻 | 固定 ManagedWimLib 版本；发布时显式加载 exe 同级 `libwim-15.dll` 并做初始化烟测 |
| DiscUtils 对 UEFI 启动支持 | ISO 不可引导 | 充分测试，必要时回退到 oscdimg |
| 断点续传文件损坏 | 下载文件不可用 | 完成后强制 SHA-256 校验；提供重新下载选项 |
| 大文件处理（>4GB） | 内存溢出 | 使用流式处理，避免将整个文件加载到内存 |
| Unpackaged 原生 DLL 布局 | libwim-15.dll 加载失败 | 确保 DLL 随发布目录输出，并与应用进程架构一致 |
| WIM 输出体积增大 | ESD→WIM 后文件更大 | 在 UI 上提示用户"WIM 比 ESD 大约大 30%"，并提供预估大小 |

---

## 九、总结

本路线图将 WindowsImageDownloader 的开发分为 **5 个阶段**：

| 阶段 | 内容 | 工期 | 状态 |
|-----|------|------|------|
| 1. 项目初始化 | 搭建骨架、依赖、MVVM 框架 | 1-2 天 | ✅ **已完成** |
| 2. 产品目录浏览 | 从微软获取产品列表，二级联动选择 | 3-4 天 | ✅ **已完成** |
| 3. 下载+转换流水线 | **核心功能**：断点续传下载 + wimlib WIM/ESD 处理 + DiscUtils ISO 创建 + 多格式输出编排 | 8-10 天 | 🔴 **待实现** |
| 4. UI 完善 | 主题、设置、本地化、错误处理 | 2-3 天 | ⏳ 待实现 |
| 5. 测试与发布 | 单元/集成测试、unpackaged 发布 | 2-3 天 | ⏳ 待实现 |

### 关键设计决策

1. **一站式工作流**：用户在目录页选择产品后，通过复选框自由组合需要的输出格式（ESD/WIM/ISO）。系统自动处理下载 → 校验 → 多格式转换的完整流程。

2. **零额外依赖**：
   - WIM/ESD 处理：使用 **wimlib** 的单一 DLL（`libwim-15.dll`，~2MB），随应用附带
   - ISO 创建：使用 **DiscUtils** NuGet 包（纯 .NET 代码，编译后几百 KB）
   - **不需要安装 Windows ADK 或其他任何外部工具**

3. **WIM 支持几乎零成本**：wimlib 原生支持 ESD ↔ WIM 互转（LZMS ↔ LZX），代码改动只是改一个压缩参数。WIM 输出为**企业部署**和 **DIY 定制镜像**提供了关键能力。

4. **应用退出不丢失进度**：下载状态、已下载字节数、转换进度都持久化到 SQLite，重新启动后自动恢复。

5. **Unpackaged 发布**：最终输出为可直接分发的文件夹/ZIP 或传统安装器，不使用 MSIX，也不启用 AOT。
