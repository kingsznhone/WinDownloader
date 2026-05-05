namespace POC.Models;

public sealed record EsdToIsoRequest(
    string SourceEsdPath,
    string OutputRoot,
    string VolumeLabel = "ESD_ISO",
    bool KeepIntermediateFiles = true);
