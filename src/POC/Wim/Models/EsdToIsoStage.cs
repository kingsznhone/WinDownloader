namespace POC.Wim.Models;

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
