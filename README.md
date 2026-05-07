# WindowsImageDownloader

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4)](https://docs.microsoft.com/en-us/windows/apps/winui/winui3/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011-0078D6)](https://www.microsoft.com/windows)

> **中文版说明请见 [README_ZH.md](README_ZH.md)**

A Windows installation image downloader built with **WinUI 3** and **.NET 10**. It fetches product catalogs from the **Microsoft Update Catalog**, filters ESD files, downloads them with multi-threaded resumable support, verifies SHA-256 checksums, persists tasks via SQLite, and optionally converts downloaded ESD files to bootable ISO images.

---

## ✨ Features

- **📦 Product Catalog Browsing** — Fetches and parses the official Microsoft Update Catalog (`products.cab` / `products.xml`), presenting available Windows images grouped by language and architecture.
- **⬇️ Multi-threaded Resumable Download** — Powered by the [Downloader](https://github.com/bezzad/Downloader) library. Supports pause, resume, and concurrent downloads with configurable limits.
- **🔐 SHA-256 Verification** — Automatically verifies the integrity of every downloaded ESD file against its published hash.
- **💾 SQLite Task Persistence** — Download tasks are persisted locally via SQLite. Tasks survive application restarts, and interrupted downloads can be resumed.
- **🔄 ESD → ISO Conversion** — After download, you can optionally convert ESD files into bootable ISO images. The conversion pipeline:
  - Extracts WIM images using **ManagedWimLib** (`WinDownloader.Wim`)
  - Reuses official solid LZMS compression for `sources\install.wim`
  - Creates the final ISO using the bundled **oscdimg** tool (`WinDownloader.Iso`)
- **🌐 Localization** — Supports **en-US** and **zh-CN** with MRT Core resources. Language switching takes effect after restart.
- **🎨 Modern WinUI 3 UI** — Built with Windows App SDK 2.0, featuring navigation view, settings page, and real-time download progress.

---

## 🖼️ Screenshots

*(Coming soon)*

---

## 🚀 Getting Started

### Prerequisites

- **Windows 11** (build 26100+)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio 2026](https://visualstudio.microsoft.com/) (recommended) with the following workloads:
  - .NET Desktop Development
  - Universal Windows Platform development
  - Windows App SDK C++ templates

### Build & Run

```powershell
# Clone the repository
git clone https://github.com/your-username/WinDownloader.git
cd WinDownloader

# Build the main WinUI application (x64 only)
dotnet build .\src\WinDownloader\WinDownloader.csproj -nologo -p:Platform=x64 -v minimal

# Run
dotnet run --project .\src\WinDownloader\WinDownloader.csproj -p:Platform=x64
```

> **Note:** The main application currently targets **x64** only. The platform must be explicitly specified when building.

### Publish

```powershell
dotnet publish .\src\WinDownloader\WinDownloader.csproj -nologo -p:Platform=x64 -p:PublishDir=.\publish -v minimal
```

---

## 🏗️ Project Structure

```
src/
├── WinDownloader/              # Main WinUI 3 application
│   ├── App.xaml / .cs          # Host, DI, application lifecycle
│   ├── MainWindow.xaml / .cs   # NavigationView shell
│   ├── Interfaces/             # Service contracts
│   ├── Models/                 # Data models (DownloadTask, TaskState, etc.)
│   ├── Services/               # Service implementations
│   ├── ViewModels/             # MVVM ViewModels
│   ├── Views/                  # Pages and controls
│   └── Strings/                # Localization resources (.resw)
├── WinDownloader.Wim/          # ManagedWimLib wrapper library
├── WinDownloader.Iso/          # ISO packaging library (oscdimg wrapper)
├── POC/                        # Console proof-of-concept host
└── WinDownloader.slnx          # Solution file
```

---

## 🧱 Architecture Overview

| Layer | Technology |
|-------|-----------|
| UI Framework | WinUI 3 (`Microsoft.UI.Xaml`) |
| Runtime | .NET 10 + Windows App SDK 2.0 |
| MVVM | CommunityToolkit.Mvvm |
| DI / Lifecycle | Microsoft.Extensions.Hosting + DI |
| Download Engine | [Downloader](https://github.com/bezzad/Downloader) NuGet |
| WIM Processing | [ManagedWimLib](https://github.com/kingseva/ManagedWimLib) |
| ISO Creation | Bundled `oscdimg.exe` |
| Database | Microsoft.Data.Sqlite |
| Settings | JSON file |

### Data Flow

```
Selection Page
  → Fetch catalog from Microsoft Update Catalog
  → Parse products.xml → RawFile list
  → User selects & queues downloads
  → DownloadTaskOrchestratorService schedules tasks
  → EsdDownloadPipeline downloads + SHA-256 verifies
  → SQLite persists task state
  → (Optional) EsdToIsoConversionService converts ESD → ISO
```

---

## 📚 Documentation

| Document | Description |
|----------|-------------|
| [docs/WORKFLOW.md](docs/WORKFLOW.md) | Standard workflow for AI agent |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Overall architecture, DI, data flow |
| [docs/MODULE_CATALOG.md](docs/MODULE_CATALOG.md) | Product catalog fetching |
| [docs/MODULE_DOWNLOAD.md](docs/MODULE_DOWNLOAD.md) | ESD download, SHA-256, SQLite, task orchestration |
| [docs/MODULE_CONVERSION.md](docs/MODULE_CONVERSION.md) | ESD → ISO conversion pipeline |
| [docs/MODULE_WIM.md](docs/MODULE_WIM.md) | `WinDownloader.Wim` shared library |
| [docs/MODULE_ISO.md](docs/MODULE_ISO.md) | `WinDownloader.Iso` shared library |
| [docs/MODULE_MODELS.md](docs/MODULE_MODELS.md) | Data models |
| [docs/MODULE_UI.md](docs/MODULE_UI.md) | WinUI pages, controls, ViewModels |
| [docs/MODULE_SETTINGS.md](docs/MODULE_SETTINGS.md) | Settings service and settings page |
| [docs/MODULE_LOCALIZATION.md](docs/MODULE_LOCALIZATION.md) | Localization with MRT Core |
| [docs/MODULE_PACKAGING.md](docs/MODULE_PACKAGING.md) | Packaging and publishing |
| [docs/MODULE_POC.md](docs/MODULE_POC.md) | POC console host |

---

## 🧪 Proof-of-Concept (POC)

The `src/POC` directory contains a console application that directly references `WinDownloader.Wim` and `WinDownloader.Iso`. It serves as a validation and experimentation host for:

- Testing WIM/ISO conversion logic independently
- Experimenting with progress mapping and compression parameters
- Validating oscdimg behavior

```powershell
dotnet run --project .\src\POC\POC.csproj
```

---

## 🤝 Contributing

Contributions are welcome! Please read our [contributing guidelines](docs/WORKFLOW.md) first.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

---

## 📚 References

- [Downloader](https://github.com/bezzad/Downloader) — Multi-threaded download library
- [ManagedWimLib](https://github.com/kingseva/ManagedWimLib) — .NET wrapper for wimlib
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MVVM toolkit
- [CommunityToolkit.WinUI](https://github.com/CommunityToolkit/Windows) — WinUI controls
- [oscdimg](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/oscdimg-command-line-options) — Windows ISO creation tool
- [Microsoft Update Catalog](https://www.catalog.update.microsoft.com/) — Windows image source
