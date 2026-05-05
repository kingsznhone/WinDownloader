using ManagedWimLib;

namespace WindowsImageDownloader.Models;

public sealed record EsdToIsoRequest(
    string SourceEsdPath,
    string StagingRoot,
    string VolumeLabel = "ESD_ISO",
    bool KeepIntermediateFiles = true,
    CompressionType InstallCompression = CompressionType.LZMS);
