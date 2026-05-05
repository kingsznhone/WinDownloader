namespace POC.Models;

public enum EsdToIsoStage
{
    Preparing,
    InspectingSource,
    ApplyingSetupMedia,
    BuildingBootWim,
    BuildingInstallImage,
    CreatingIso,
    Completed,
    Failed
}
