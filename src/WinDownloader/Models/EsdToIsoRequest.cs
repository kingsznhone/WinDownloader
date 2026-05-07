using ManagedWimLib;

namespace WinDownloader.Models;

public sealed record EsdToIsoRequest(
    string SourceEsdPath,
    string StagingDirectory,
    string VolumeLabel = "ESD_ISO",
    bool KeepIntermediateFiles = true,
    CompressionType InstallCompression = CompressionType.LZMS,
    bool RecompressInstallImage = false);
