# WindowsImageDownloader POC

This project is the concept-verification area for image post-processing work that is intentionally kept outside the WinUI app.

The POC is now shaped like the future WinUI post-processing service:

- `Program.cs` is a minimal console host. It parses a small option set, creates services, subscribes to task snapshots, and calls `EsdToIsoConversionService.ConvertAsync()`.
- `EsdToIsoConversionService` owns the full ESD-to-ISO workflow and publishes immutable `EsdToIsoTaskSnapshot` updates through `ProgressChanged`.
- `Program.cs` subscribes to `ProgressChanged` directly and formats snapshots for console output.
- Console output is mirrored to a `console-*.log` file by `Program.cs`; the conversion service does not write manifest or summary files.
- WIM and ISO details stay behind `WimProcessingService` and `OscdimgIsoCreationService`.

Useful commands:

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj --
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\install.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\install.esd --delete-intermediate
```

Supported options:

| Option | Default | Description |
|--------|---------|-------------|
| `--source <path>` | local hardcoded test ESD | Source ESD path |
| `--output-root <path>` | `<source directory>\poc-iso-output` | Root folder for run outputs |
| `--volume-label <label>` | `ESD_ISO` | ISO volume label |
| `--keep-intermediate` | enabled | Keep staging files after conversion |
| `--delete-intermediate` | disabled | Delete staging files after a successful conversion |

Fixed pipeline mapping:

- ESD image 1 -> ISO staging root.
- ESD image 2 + 3 -> `sources\boot.wim` with image 3 marked bootable.
- ESD image 4+ -> `sources\install.esd`.
- `oscdimg` creates `oscdimg.iso` in the run directory.

Each run writes:

- `staging\` with the temporary ISO file tree.
- `staging\sources\boot.wim`.
- `staging\sources\install.esd`.
- `oscdimg.iso`.
- `console-*.log` in the output root.

The POC no longer exposes install format selection, ISO backend selection, or default `events.ndjson`/manifest/summary logging. The main `WindowsImageDownloader` app should still stay focused on catalog browsing, ESD download, SHA-256 verification, and task management until post-processing is deliberately migrated.
