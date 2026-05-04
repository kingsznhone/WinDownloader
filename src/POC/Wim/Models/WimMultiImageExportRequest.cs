namespace POC.Wim.Models;

public sealed record WimMultiImageExportRequest(
    string SourceImagePath,
    string DestinationImagePath,
    IReadOnlyList<WimImageExportItem> Images,
    WimCompressionKind Compression = WimCompressionKind.LZX,
    bool CheckIntegrity = true,
    bool Recompress = true,
    bool Solid = false,
    uint OutputChunkSize = 0,
    uint OutputPackChunkSize = 0);
