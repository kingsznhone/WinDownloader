namespace POC.Models;

public enum EsdToIsoStage
{
    Preparing,
    InspectingSource,
    ApplyingSetupMedia,
    BuildingBootWim,
    BuildingInstallImage,
    CreatingIso,
    WritingManifest,
    Completed,
    Failed
}
