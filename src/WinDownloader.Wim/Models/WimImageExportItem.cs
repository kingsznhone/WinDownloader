using ManagedWimLib;

namespace WinDownloader.Wim;

public sealed record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    ExportFlags ExportFlags = ExportFlags.None);
