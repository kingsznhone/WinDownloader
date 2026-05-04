using POC.Wim.Models;

namespace POC.Wim.Services;

internal sealed record InstallTarget(
    string Path,
    WimCompressionKind Compression,
    bool Solid,
    uint ChunkSize,
    uint PackChunkSize);
