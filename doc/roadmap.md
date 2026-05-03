# WindowsImageDownloader WinUI 3 App 开发路线图

## 📋 概述

本项目基于两个 Shell 脚本（`download-windows-esd.txt` 和 `windows-esd-to-iso.txt`）的功能，计划构建一个功能完整的 **WinUI 3** 桌面应用程序，用于**查询、下载 Windows ESD 映像**，并可选择在下载完成后**自动转换为 WIM 格式或 ISO 安装介质**（支持自由组合输出）。

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

WIM 格式特别适合**企业批量部署（MDT/SCCM）** 和**DIY 定制系统**的场景——它支持直接挂载编辑，而 ESD 和 ISO 都不行。

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
  ① 打开 App → ② 浏览目录（选语言 → 选版本 → 选架构）
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
│  │  │ [语言 ▼] [版本 ▼] [架构 ▼]    ↓  │  │       │
│  │  │   三级联动选择器，筛选可用映像      │  │       │
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
├── App.xaml / App.xaml.cs            # 应用入口
├── MainWindow.xaml / .cs             # 主窗口
├── app.manifest                      # 应用清单
├── Package.appxmanifest              # 模板残留；unpackaged 发布不使用
├── WindowsImageDownloader.csproj     # 项目文件
│
├── Models/                           # 数据模型
│   ├── ProductInfo.cs                # 产品信息
│   ├── WindowsImage.cs               # Windows 映像（语言/版本/架构）
│   ├── DownloadTask.cs               # 下载任务（包括目标格式、转换状态）
│   └── TaskStatus.cs                 # 任务状态枚举
│
├── Services/                         # 核心服务层
│   ├── IUpdateCatalogService.cs      # 更新目录服务接口
│   ├── UpdateCatalogService.cs       # 更新目录服务实现（从 Microsoft 获取 XML）
│   ├── ICacheService.cs              # 缓存服务接口
│   ├── CacheService.cs               # 缓存服务实现（SQLite）
│   ├── IDownloadService.cs           # 下载服务接口
│   ├── DownloadService.cs            # 下载服务实现（断点续传 + 队列管理）
│   ├── IWimProcessingService.cs      # WIM 处理服务接口（ManagedWimLib）
│   ├── WimProcessingService.cs       # WIM 处理服务实现
│   ├── IIsoCreationService.cs        # ISO 创建服务接口（DiscUtils）
│   ├── IsoCreationService.cs         # ISO 创建服务实现
│   ├── ITaskOrchestratorService.cs   # 任务编排服务接口
│   └── TaskOrchestratorService.cs    # 任务编排：下载→转换流水线
│
├── ViewModels/                       # ViewModel 层（MVVM）
│   ├── MainViewModel.cs              # 主 ViewModel
│   ├── CatalogViewModel.cs           # 目录页 ViewModel
│   ├── DownloadListViewModel.cs      # 下载任务列表 ViewModel
│   └── SettingsViewModel.cs          # 设置页 ViewModel
│
├── Views/                            # UI 页面
│   ├── Pages/
│   │   ├── CatalogPage.xaml / .cs    # 产品目录浏览页（选产品 + 选格式 + 添加任务）
│   │   ├── DownloadListPage.xaml / .cs # 下载任务列表页（含自动转换进度）
│   │   └── SettingsPage.xaml / .cs   # 设置页
│   └── Controls/
│       ├── TaskItemControl.xaml / .cs     # 单个任务卡片（进度、状态、操作按钮）
│       ├── FormatSelectorControl.xaml / .cs # 输出格式选择器（ESD/WIM/ISO 多选）
│       └── LogOutputControl.xaml / .cs    # 实时日志输出控件（用于转换过程）
│
├── Helpers/                          # 辅助工具
│   ├── XmlParser.cs                  # Products.xml 解析器
│   ├── Sha256Helper.cs               # SHA-256 计算工具
│   ├── FileSizeFormatter.cs          # 文件大小格式化
│
├── Resources/                        # 资源文件与 native 依赖输入
│   ├── libwim-15.dll                 # wimlib Windows DLL（发布到 exe 同级）
│   └── Strings/
│       └── zh-CN/                    # 中文本地化
│
└── Assets/                           # 应用资源
    ├── ...
    └── MicrosoftRootCA2011.cer       # 微软根证书（用于 HTTPS 验证）
```

---

## 五、开发路线图（按阶段）

### 阶段 1：项目初始化与环境搭建 ⏱ 1-2 天

- [ ] 1.1 确认开发环境
  - Visual Studio 2022+ 安装 WinUI 工作负载
  - Windows App SDK 版本确认（当前项目使用 2.0.1）
  - .NET 10 环境配置
- [ ] 1.2 项目结构搭建
  - 按上述架构创建文件夹和类文件骨架
  - 安装 NuGet 包依赖
- [ ] 1.3 基础 MVVM 框架
  - 集成 CommunityToolkit.Mvvm（`[ObservableProperty]`、`[RelayCommand]` 等源码生成器）
  - 配置依赖注入（Microsoft.Extensions.DependencyInjection）
  - 配置 NavigationView 导航框架

**NuGet 依赖：**
```xml
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
<PackageReference Include="CommunityToolkit.WinUI.UI.Controls" Version="7.*" />
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
<PackageReference Include="Discutils" Version="0.*" />
```

---

### 阶段 2：产品目录浏览功能 ⏱ 3-4 天

- [ ] 2.1 `UpdateCatalogService` 实现
  - 分析 `download-windows-esd.txt` 中的 REST API 调用逻辑
  - 用 `HttpClient` 调用 Windows Update 服务：`POST https://fe3.delivery.mp.microsoft.com/UpdateMetadataService/updates/search/v1/bydeviceinfo`
  - 请求体：`{"Products": "PN=Windows.Products.Cab.amd64&V=26100.0.0.0", "DeviceAttributes": "DUScan=1;OSVersion=10.0.026100.1"}`
  - 响应解析：从 JSON 中提取 `FileLocations[0].Url` 和 `FileLocations[0].Digest`
- [ ] 2.2 下载 products.cab
  - 支持 `If-Modified-Since` 条件请求（对应 `--time-cond`）
  - 下载后校验 SHA-256（Base64 编码比较）
  - 使用 `System.IO.Compression` 解压 CAB 中的 `products.xml`
- [ ] 2.3 证书处理
  - 将 Microsoft Root CA 2011 证书嵌入应用资源
  - 配置 `HttpClientHandler.ServerCertificateCustomValidationCallback` 或导入受信任根证书
- [ ] 2.4 `CacheService` 实现
  - 基于 SQLite 缓存 products.xml 解析结果
  - 缓存策略：24 小时内不过期（对应 `-mmin -1440`）
  - 缓存版本管理
- [ ] 2.5 Products.xml 解析器
  - 使用 `XDocument` / `XPath` 解析 products.xml
  - 提取：`LanguageCode`、`Edition`、`Architecture`、`FileName`、`FilePath`、`Sha256`
- [ ] 2.6 `CatalogPage` UI 实现
  - 三级联动选择器：语言 → 版本 → 架构
  - 显示文件信息（文件名、大小、SHA-256、下载链接）
  - **输出格式选择器（多选复选框）**：
    - ☑ ESD 文件（原始下载）
    - ☐ WIM 文件（LZX 重压缩，可挂载编辑）
    - ☐ ISO 镜像（可启动安装盘）
  - "[添加下载任务]" 按钮

---

### 阶段 3：下载 + 自动转换一站式流水线 ⏱ 8-10 天

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

- [x] 3.2.1 ManagedWimLib 集成
  - 添加 `ManagedWimLib` NuGet 包
  - 使用 `Resources/libwim-15.dll` 作为 custom native binary，并发布到 exe 同级
  - 封装为 `IWimProcessingService` / `WimProcessingService`，UI 不直接依赖 ManagedWimLib 类型
- [ ] 3.2.2 实现 WIM 操作核心方法
  - `GetImagesAsync(esdPath)` → 获取映像数量和映像信息（对应 `wiminfo --header`）
  - `ExtractImageAsync(esdPath, index, destDir)` → 解压映像到目录（对应 `wimapply`）
  - `ExportImageAsync(request)` → 导出映像（对应 `wimexport`）
  - 支持两种压缩算法：
    - `WimCompression.LZMS` → 输出 ESD 格式（固态归档 + 高压缩）
    - `WimCompression.LZX` → 输出 WIM 格式（标准压缩，可挂载编辑）
- [ ] 3.2.3 临时目录管理
  - 使用 `System.IO.Path.GetTempPath()` 创建临时工作目录
  - 安全清理机制（对应 Shell 的 `trap cleanup EXIT`）
  - 提供保留临时文件选项（对应 `NO_CLEANUP`，用于调试）

#### 3.3 ISO 创建引擎（DiscUtils）

- [ ] 3.3.1 DiscUtils 集成与封装
  - 使用 `DiscUtils.Iso9660.CDBuilder` 创建 ISO
  - **无需任何外部工具，纯 .NET 代码**
- [ ] 3.3.2 实现 ISO 创建核心方法
  - `CreateBootableIso(sourceDir, outputPath, volumeName)` → 创建 UEFI 可启动 ISO
  - 设置 El Torito EFI 启动入口（指向 `efi/microsoft/boot/efisys.bin`）
  - 设置 ISO 9660 卷标 + UDF 桥接卷标
- [ ] 3.3.3 进度报告
  - 转换阶段分步进度（解压中 / 导出 boot.wim / 导出 install.esd/wim / 打包 ISO）
  - 整体 ETA 估算

#### 3.4 任务编排引擎

- [ ] 3.4.1 `TaskOrchestratorService` 实现
  - 负责任务的完整生命周期：等待下载 → 下载中 → SHA-256校验 → (如需WIM/ISO) → 转换中 → 完成
  - 根据输出格式决定后续步骤：
    - 仅 ESD → 下载完成 + 校验通过 → 完成
    - ESD + WIM → 下载完成 + 校验通过 → 用 LZX 重压缩为 WIM → 完成
    - ESD + ISO → 下载完成 + 校验通过 → 用 DiscUtils 生成 ISO → 完成
    - ESD + WIM + ISO → 三步走：保留 ESD → 转 WIM → 用 WIM 制作 ISO → 全部完成
- [ ] 3.4.2 状态持久化
  - 应用关闭后恢复任务状态
  - 断点续传 + 转换断点（如果某格式已生成则跳过）

#### 3.5 任务列表 UI

- [ ] 3.5.1 `DownloadListPage` UI 实现
  - 每个任务一张卡片，显示：
    - 标题：Windows 11 Pro zh-cn amd64
    - 输出格式标识：📦 ESD  / 📁 WIM / 💿 ISO / 组合（如 📦📁💿）
    - 多格式转换时显示子任务进度（如 "WIM: 60% | ISO: 40%"）
    - 进度条 + 百分比 + 速度/ETA
    - 状态文字：下载中 / 校验中 / 转换中 / 已完成 / 失败
    - 操作按钮：暂停/恢复/取消/打开文件夹/重试
  - 排序：按添加时间倒序
  - 工具栏：清除已完成、清除全部（带确认）

---

### 阶段 4：用户界面完善 ⏱ 2-3 天

- [ ] 4.1 主题与样式
  - 支持浅色/深色主题（WinUI 3 原生支持）
  - 自定义控件样式
  - 图标与视觉资产
- [ ] 4.2 设置页面
  - 下载目录设置
  - 临时目录设置
  - 同时下载任务数限制
  - 默认输出格式（ESD / ESD+WIM / ESD+ISO / 全选）
  - 代理设置
  - 缓存清理按钮
  - 语言选择（中文/English）
- [ ] 4.3 任务历史
  - 已完成任务列表（含结果信息）
  - 清除历史功能
- [ ] 4.4 错误处理与用户体验
  - 全局异常处理
  - 网络错误自动重试（可配置重试次数）
  - 友好的错误提示而非终端报错
  - 操作确认对话框（取消下载/删除文件）
  - 系统托盘 + 下载完成通知（Toast）
- [ ] 4.5 本地化
  - 支持中英文界面
  - 使用 `.resw` 资源文件

---

### 阶段 5：测试与发布 ⏱ 2-3 天

- [ ] 5.1 单元测试
  - Services 层单元测试
  - XML 解析测试
  - SHA-256 校验测试
  - WIM 导出测试（ESD→WIM 重压缩）
  - DiscUtils ISO 创建测试
- [ ] 5.2 集成测试
  - 完整工作流测试：选产品 → 下载 → 自动转换 → 输出三种格式
  - 断点续传测试（中断网络再恢复）
  - 取消任务测试
  - 应用重启后任务恢复测试
  - WIM 可挂载性验证（输出 .wim 能否被 `dism /Mount-Wim` 挂载）
- [ ] 5.3 Unpackaged 发布
  - 使用文件系统发布目录，不生成 MSIX
  - 保持 `WindowsPackageType=None`，不启用 AOT、trim 或单文件发布
  - 确认 `libwim-15.dll` 和 Windows App SDK self-contained 文件随发布目录输出
  - 应用签名（可选，但推荐用于减少 SmartScreen 提示）
- [ ] 5.4 发布
  - GitHub Releases 发布
  - unpackaged ZIP/安装器发布形式
  - 安装说明文档
  - 用户指南

---

## 六、关键实现细节

### 6.1 Windows Update REST API 调用

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

### 6.2 断点续传下载

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

### 6.3 products.xml 解析

```csharp
XDocument doc = XDocument.Parse(xmlContent);
XNamespace ns = doc.Root.GetDefaultNamespace();

// 查询所有语言
var languages = doc.Descendants(ns + "LanguageCode")
    .Select(x => x.Value)
    .Distinct()
    .OrderBy(x => x);

// 查询指定语言+版本的架构
var architectures = doc.Descendants(ns + "File")
    .Where(f => f.Element(ns + "LanguageCode")?.Value == language
             && f.Element(ns + "Edition")?.Value == edition)
    .Elements(ns + "Architecture")
    .Select(x => x.Value)
    .Distinct()
    .OrderBy(x => x);

// 获取文件信息
var fileInfo = doc.Descendants(ns + "File")
    .FirstOrDefault(f => f.Element(ns + "LanguageCode")?.Value == language
                      && f.Element(ns + "Edition")?.Value == edition
                      && f.Element(ns + "Architecture")?.Value == architecture);
var fileName = fileInfo?.Element(ns + "FileName")?.Value;
var fileUrl = fileInfo?.Element(ns + "FilePath")?.Value;
var sha256 = fileInfo?.Element(ns + "Sha256")?.Value;
```

### 6.4 ESD → WIM 转换（wimlib LZX 重压缩）

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

### 6.5 ESD → ISO 转换核心逻辑（wimlib + DiscUtils）

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
        // 如果同时也要求输出 WIM，此处也可改为 LZX 输出 install.wim
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

### 6.6 任务模型（支持多格式输出）

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

## 七、风险与挑战

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

## 八、总结

本路线图将 WindowsImageDownloader 的开发分为 **5 个阶段**：

| 阶段 | 内容 | 工期 |
|-----|------|------|
| 1. 项目初始化 | 搭建骨架、依赖、MVVM 框架 | 1-2 天 |
| 2. 产品目录浏览 | 从微软获取产品列表，三级联动选择 | 3-4 天 |
| 3. 下载+转换流水线 | **核心功能**：断点续传下载 + wimlib WIM/ESD 处理 + DiscUtils ISO 创建 + 多格式输出编排 | 8-10 天 |
| 4. UI 完善 | 主题、设置、本地化、错误处理 | 2-3 天 |
| 5. 测试与发布 | 单元/集成测试、unpackaged 发布 | 2-3 天 |
| **总计** | | **16-22 天** |

### 关键设计决策

1. **一站式工作流**：用户在目录页选择产品后，通过复选框自由组合需要的输出格式（ESD/WIM/ISO）。系统自动处理下载 → 校验 → 多格式转换的完整流程。

2. **零额外依赖**：
   - WIM/ESD 处理：使用 **wimlib** 的单一 DLL（`libwim-15.dll`，~2MB），随应用附带
   - ISO 创建：使用 **DiscUtils** NuGet 包（纯 .NET 代码，编译后几百 KB）
   - **不需要安装 Windows ADK 或其他任何外部工具**

3. **WIM 支持几乎零成本**：wimlib 原生支持 ESD ↔ WIM 互转（LZMS ↔ LZX），代码改动只是改一个压缩参数。WIM 输出为**企业部署**和 **DIY 定制镜像**提供了关键能力。

4. **应用退出不丢失进度**：下载状态、已下载字节数、转换进度都持久化到 SQLite，重新启动后自动恢复。

5. **Unpackaged 发布**：最终输出为可直接分发的文件夹/ZIP 或传统安装器，不使用 MSIX，也不启用 AOT。
