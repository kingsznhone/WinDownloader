namespace POC.Wim.Models;

public sealed record EsdToIsoRequest(
    string SourceEsdPath,
    string OutputRoot,
    InstallImageFormat InstallFormat = InstallImageFormat.Esd,
    IsoCreationBackend IsoBackend = IsoCreationBackend.Both,
    string VolumeLabel = "ESD_ISO",
    bool KeepIntermediateFiles = true);
