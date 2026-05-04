# WindowsImageDownloader POC

This project is the concept-verification area for image post-processing work that is intentionally kept outside the WinUI app.

Current contents:

- `Wim/` contains the copied ManagedWimLib-based WIM processing wrapper.
- ISO creation experiments can be added here without affecting the main downloader.

The main `WindowsImageDownloader` app should stay focused on catalog browsing, ESD download, SHA-256 verification, and task management.
