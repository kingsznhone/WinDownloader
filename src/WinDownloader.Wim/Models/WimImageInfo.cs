namespace WinDownloader.Wim;

public sealed record WimImageInfo(
    int Index,
    string Name,
    string Description,
    string DisplayName,
    string EditionId,
    string InstallationType,
    string Architecture,
    string DefaultLanguage,
    long TotalBytes,
    bool IsBootable)
{
    public string Title => !string.IsNullOrWhiteSpace(DisplayName)
        ? DisplayName
        : !string.IsNullOrWhiteSpace(Name) ? Name : $"Image {Index}";

    public string Subtitle => string.Join(" / ", new[] { EditionId, InstallationType, Architecture, DefaultLanguage }
        .Where(static value => !string.IsNullOrWhiteSpace(value)));
}
