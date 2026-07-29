# Changelog

All notable changes to WindowsImageDownloader are documented in this file.

## [1.1.0] - 2026-07-29

### Added

- Added a separate runtime environment section for users running a published build.
- Added a separate development environment section with SDK, Visual Studio workload, Git, and NuGet requirements.
- Added source-build instructions for restoring dependencies before compiling the WinUI application.

### Changed

- Updated the main application and shared libraries to version `1.1.0`.
- Documented that published builds use Windows App SDK self-contained deployment and do not require the .NET SDK or Visual Studio on the target machine.
- Clarified the x64 and Windows 11 build 26100 requirements in the README files.

### Fixed

- Removed an accidental trailing character from the `sources\\install.wim` entry in the Chinese README.

## [1.0.0]

- Initial release.
