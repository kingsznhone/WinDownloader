# WindowsImageDownloader POC

This project is the concept-verification area for image post-processing work that is intentionally kept outside the WinUI app.

Current contents:

- `Program.cs` hardcodes the current local ESD experiment source and runs a focused ESD-to-ISO validation pipeline.
- `Wim/` contains ManagedWimLib wrappers, pipeline models, progress/event logging, and two ISO creation backends.
- Intermediate staging/WIM/ESD outputs are intentionally kept for inspection.

Useful commands:

```powershell
dotnet run --project .\src\POC\POC.csproj -- --inspect-only
dotnet run --project .\src\POC\POC.csproj -- --install-format esd --iso-backend both
dotnet run --project .\src\POC\POC.csproj -- --install-format both --iso-backend oscdimg --output-root D:\IsoPoc
```

Pipeline mapping:

- ESD image 1 -> ISO staging root.
- ESD image 2 + 3 -> `sources\boot.wim` with image 3 marked bootable.
- ESD image 4+ -> `sources\install.esd` or `sources\install.wim`.
- `oscdimg` is the compatibility-oriented backend; `DiscUtils` is an experimental pure managed comparison backend.

The main `WindowsImageDownloader` app should stay focused on catalog browsing, ESD download, SHA-256 verification, and task management.
