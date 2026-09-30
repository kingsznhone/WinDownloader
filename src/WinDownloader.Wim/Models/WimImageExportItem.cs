using ManagedWimLib;

namespace WinDownloader.Wim.Models;

public sealed record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    ExportFlags ExportFlags = ExportFlags.None);
