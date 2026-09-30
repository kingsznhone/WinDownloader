# WindowsImageDownloader POC

Console validation host for ESD-to-ISO conversion, compression, progress and oscdimg diagnostics. `Program.cs` parses options and mirrors console output to a log; `CliConversionService` runs the pipeline through the shared WIM/ISO libraries and reports `IProgress<CliConversionProgress>`.

Useful commands:

```powershell
dotnet run --project .\src\POC\POC.csproj -- --help
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --output-root D:\IsoPoc
dotnet run --project .\src\POC\POC.csproj -- --source C:\Path\To\source.esd --recompress-install-image
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

Image layout and host differences are documented in [the POC module](../../docs/MODULE_POC.md).

Each run writes:

- `staging\` under `--output-root` with the temporary ISO file tree.
- `staging\sources\boot.wim`.
- `staging\sources\install.wim`.
- `<source file name>.iso` beside the source ESD.
- `console-*.log` in `--output-root`.

Ctrl+C cancels conversion. Staging is retained by default; `--delete-staging` removes it after success. No manifest or summary files are generated.
