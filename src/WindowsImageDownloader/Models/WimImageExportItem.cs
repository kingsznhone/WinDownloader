using ManagedWimLib;

namespace WindowsImageDownloader.Models;

public sealed record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    ExportFlags ExportFlags = ExportFlags.None);
