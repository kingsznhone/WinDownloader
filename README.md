# WindowsImageDownloader

WindowsImageDownloader 是一个基于 WinUI 3 和 .NET 10 的 Windows 安装映像下载工具。主应用负责从 Microsoft Update Catalog 获取产品目录、筛选 ESD 文件、多线程断点续传下载、SHA-256 校验、SQLite 任务持久化、下载任务 UI 管理，并在 ESD 下载完成后提供可选的 ESD 到 ISO 转换。

## 当前范围

- 主项目 `src/WindowsImageDownloader`：WinUI 图形应用，包含 ESD 下载流水线和 ISO 转换入口。
- WIM/ISO 共享库：`src/WindowsImageDownloader.Wim` 封装 ManagedWimLib，`src/WindowsImageDownloader.Iso` 封装 ESD→ISO 流水线和 oscdimg 后端。
- ISO 转换：默认把 ESD image 4..n 复用官方 solid LZMS 资源写入 `sources\install.wim`，使用随宿主复制输出的 `Oscdimg\oscdimg.exe` 创建 ISO。
- POC 项目 `src/POC`：保留为控制台验证和对照宿主，用于快速试验共享转换库、进度映射和 oscdimg 行为。
- 当前没有把 ISO 转换状态写入 SQLite，也没有把下载任务 `TaskState` 扩展为 `Converting`；转换状态通过独立快照传给 UI。

## 构建

主 WinUI 项目需要显式指定 Windows 平台架构：

```powershell
dotnet build .\src\WindowsImageDownloader\WindowsImageDownloader.csproj -nologo -p:Platform=x64 -v minimal
```

## 文档入口

从 [docs/WORKFLOW.md](docs/WORKFLOW.md) 开始阅读；总体结构见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。本地化资源、PRI 行为和语言切换规则见 [docs/MODULE_LOCALIZATION.md](docs/MODULE_LOCALIZATION.md)。
