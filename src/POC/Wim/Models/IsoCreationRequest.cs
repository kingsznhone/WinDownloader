namespace POC.Wim.Models;

public sealed record IsoCreationRequest(
    string StagingDirectory,
    string OutputIsoPath,
    string VolumeLabel,
    IsoCreationBackend Backend);
