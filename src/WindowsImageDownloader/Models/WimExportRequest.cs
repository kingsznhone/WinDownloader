using ManagedWimLib;

namespace WindowsImageDownloader.Models;

public sealed record WimExportRequest(
    string SourceImagePath,
    string DestinationImagePath,
    IReadOnlyList<WimImageExportItem> Images,
    CompressionType Compression = CompressionType.LZX,
    bool CheckIntegrity = true,
    bool Recompress = true,
    bool Solid = false,
    uint OutputChunkSize = 0,
    uint OutputPackChunkSize = 0);
