# WindowsImageDownloader POC

This project is the console validation and comparison host for the ESD-to-ISO pipeline. The WinUI app now has the product-facing ISO conversion entry; this POC remains useful for debugging progress mapping, compression choices, staging cleanup, and oscdimg output without launching the UI.

The POC is shaped like the WinUI conversion service stack:

- `Program.cs` is a minimal console host. It parses a small option set, creates services, subscribes to task snapshots, and calls `EsdToIsoConversionService.ConvertAsync()`.
- `EsdToIsoConversionService`, `WimProcessingService`, and `OscdimgIsoCreationService` come from the shared `WindowsImageDownloader.Iso` and `WindowsImageDownloader.Wim` projects.
- The conversion service owns the full ESD-to-ISO workflow and publishes immutable `EsdToIsoTaskSnapshot` updates through `ProgressChanged`.
- `Program.cs` subscribes to `ProgressChanged` directly and formats snapshots for console output.
- Console output is mirrored to a `console-*.log` file by `Program.cs`; the conversion service does not write manifest or summary files.
- WIM and ISO details stay behind the shared services; POC only hosts and logs them.

Useful commands:

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj --
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --delete-staging
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --recompress-install-image
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --install-compression LZX
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc --iso-only
```

Supported options:

| Option | Default | Description |
|--------|---------|-------------|
| `--source <path>` | local hardcoded test ESD | Source ESD path |
| `--output-root <path>` | `<source directory>\poc-iso-staging` | Staging root and console log folder |
| `--volume-label <label>` | `ESD_ISO` | ISO volume label |
| `--delete-staging` | disabled | Delete staging files after a successful conversion |
| `--install-compression <value>` | `LZMS` | Compression algorithm for `install.wim`; `LZX` forces recompression |
| `--reuse-install-resources` | enabled | Build `install.wim` by reusing official solid LZMS ESD resources |
| `--recompress-install-image` | disabled | Force `install.wim` recompression for benchmarking or alternate compression choices |
| `--iso-only` | disabled | Skip WIM/ESD building and only package an existing staging directory |

The default install image path reuses official solid LZMS resources into `install.wim`. For speed comparisons, run once normally, then run with `--recompress-install-image` and compare `Duration`, `install.wim size`, and final ISO behavior. The fast path requires `--install-compression LZMS`; choosing `LZX` forces recompression.

Fixed pipeline mapping:

- ESD image 1 -> ISO staging root.
- ESD image 2 + 3 -> `sources\boot.wim` with image 3 marked bootable.
- ESD image 4+ -> `sources\install.wim`.
- `oscdimg` creates `<source file name>.iso` beside the source ESD.

Each run writes:

- `staging\` under `--output-root` with the temporary ISO file tree.
- `staging\sources\boot.wim`.
- `staging\sources\install.wim`.
- `<source file name>.iso` beside the source ESD.
- `console-*.log` in `--output-root`.

The POC no longer exposes install format selection, ISO backend selection, or default `events.ndjson`/manifest/summary logging. The main `WindowsImageDownloader` app owns the user-facing download and ISO conversion flow; POC-only diagnostic behavior should not be treated as a UI product contract.
