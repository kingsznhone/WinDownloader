# WindowsImageDownloader

WindowsImageDownloader 是一个基于 WinUI 3 和 .NET 10 的 Windows 安装映像下载工具。主应用当前聚焦于 Microsoft Update Catalog 产品目录获取、ESD 文件下载、SHA-256 校验、任务持久化和下载任务管理。

## 当前范围

- 主项目 `src/WindowsImageDownloader`：ESD-only 下载应用。
- POC 项目 `src/POC`：WIM/ISO 后处理概念验证区，包含从主项目剥离出的 ManagedWimLib 包装代码。
- 主项目不再引用 ManagedWimLib、DiscUtils，也不再包含 WIM/ISO 转换 UI、状态或任务缓存字段。

## 构建

```powershell
dotnet build src/WindowsImageDownloader.slnx
```

## 文档入口

从 [docs/WORKFLOW.md](docs/WORKFLOW.md) 开始阅读；总体结构见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。
