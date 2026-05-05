# WindowsImageDownloader

WindowsImageDownloader 是一个基于 WinUI 3 和 .NET 10 的 Windows 安装映像下载工具。主应用负责从 Microsoft Update Catalog 获取产品目录、筛选 ESD 文件、多线程断点续传下载、SHA-256 校验、SQLite 任务持久化、下载任务 UI 管理，并在 ESD 下载完成后提供可选的 ESD 到 ISO 转换。

## 当前范围

- 主项目 `src/WindowsImageDownloader`：WinUI 图形应用，包含 ESD 下载流水线和 ISO 转换入口。
- ISO 转换：使用 ManagedWimLib 处理 ESD/WIM 映像，使用随主项目复制输出的 `Oscdimg` 工具创建 ISO。
- POC 项目 `src/POC`：保留为控制台验证和对照宿主，用于快速试验转换流水线、进度映射和 oscdimg 行为。
- 当前没有把 ISO 转换状态写入 SQLite，也没有把下载任务 `TaskState` 扩展为 `Converting`；转换状态通过独立快照传给 UI。

## 构建

主 WinUI 项目需要显式指定 Windows 平台架构：

```powershell
dotnet build .\src\WindowsImageDownloader\WindowsImageDownloader.csproj -nologo -p:Platform=x64 -v minimal
```

## 文档入口

从 [docs/WORKFLOW.md](docs/WORKFLOW.md) 开始阅读；总体结构见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
