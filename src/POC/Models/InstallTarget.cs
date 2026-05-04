namespace POC.Models;

internal sealed record InstallTarget(
    string Path,
    WimCompressionKind Compression,
    bool Solid,
    uint ChunkSize,
    uint PackChunkSize);
