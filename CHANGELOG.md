# Changelog

All notable changes to WindowsImageDownloader are documented in this file.

## [Unreleased]

### Changed

- Updated Microsoft.WindowsAppSDK to `2.5.1`, Downloader to `5.9.8`, and Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection, and Microsoft.Extensions.Hosting to `10.0.12`.
- Changed default download configuration to 64 chunks and 8 parallel streams; existing saved settings are preserved.
- Adopted `System.Threading.Lock` for download progress throttling, ISO worker registration, and UI snapshot merging; ISO worker scheduling now observes host shutdown cancellation.
- Enabled unsafe blocks in the main application project.
- Updated the hardcoded amd64 catalog query baseline to build `26300`.
- Set the initial window size to DPI-scaled `1080 × 720` and migrated window native calls to source-generated `LibraryImport`.

### Fixed

- Persist paused and failed download states without reusing an already canceled download token.

## [1.1.0] - 2026-07-29

### Changed

- Updated the main application to Windows App SDK `2.3.1`.

## [1.0.0]

- Initial release.
