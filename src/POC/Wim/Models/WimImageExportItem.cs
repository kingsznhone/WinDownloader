namespace POC.Wim.Models;

public sealed record WimImageExportItem(
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    bool MarkBootable = false);
