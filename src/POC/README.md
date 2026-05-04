# WindowsImageDownloader POC

This project is the concept-verification area for image post-processing work that is intentionally kept outside the WinUI app.

Current contents:

- `Program.cs` hardcodes the current local ESD experiment source and runs a focused ESD-to-ISO validation pipeline.
- `Wim/` contains ManagedWimLib wrappers, pipeline models, progress/event logging, and the oscdimg ISO creation backend.
- `Oscdimg/` contains the local oscdimg experiment tool files copied to the build output.
- Intermediate staging/WIM/ESD outputs are intentionally kept for inspection.

Useful commands:

```powershell
dotnet run --project .\src\POC\POC.csproj -- --inspect-only
dotnet run --project .\src\POC\POC.csproj -- --install-format esd --iso-backend oscdimg
dotnet run --project .\src\POC\POC.csproj -- --install-format both --iso-backend oscdimg --output-root D:\IsoPoc
```

Pipeline mapping:

- ESD image 1 -> ISO staging root.
- ESD image 2 + 3 -> `sources\boot.wim` with image 3 marked bootable.
- ESD image 4+ -> `sources\install.esd` or `sources\install.wim`.
- `oscdimg` is the compatibility-oriented backend and is loaded first from the copied `Oscdimg/` output folder.

The main `WindowsImageDownloader` app should stay focused on catalog browsing, ESD download, SHA-256 verification, and task management.
