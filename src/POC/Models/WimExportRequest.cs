namespace POC.Models;

public sealed record WimExportRequest(
    string SourceImagePath,
    string DestinationImagePath,
    int ImageIndex,
    string ImageName,
    string ImageDescription,
    WimCompressionKind Compression = WimCompressionKind.LZX,
    bool MarkBootable = false,
    bool CheckIntegrity = false);
