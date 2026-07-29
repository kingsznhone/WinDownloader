using WinDownloader.Helpers;

namespace WinDownloader.Models;

public sealed record RawFile(
    string LanguageCode,
    string Language,
    string Architecture,
    string EditionLoc,
    string Edition,
    string FileName,
    string FilePath,
    string Sha256,
    long Size,
    bool IsRetailOnly)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Edition) ? FileName : Edition;

    public string LanguageLabel => string.IsNullOrWhiteSpace(Language)
        ? LanguageCode
        : $"{LanguageCode}";

    public string EditionGroupText => EditionLoc switch
    {
        "%CLIENT%" => StringRes.Get("EditionGroup_Client"),
        "%ENTERPRISE%" => StringRes.Get("EditionGroup_Enterprise"),
        "%ENTERPRISE_N%" => StringRes.Get("EditionGroup_EnterpriseN"),
        "%BASE_CHINA%" => StringRes.Get("EditionGroup_China"),
        "" => StringRes.Get("EditionGroup_Ungrouped"),
        _ => EditionLoc
    };
    public TagType EditionGroupTagType => EditionLoc switch
    {
        "%CLIENT%" => TagType.Success,
        "%ENTERPRISE%" => TagType.Primary,
        "%ENTERPRISE_N%" => TagType.Primary,
        "%BASE_CHINA%" => TagType.Warning,
        "" => TagType.Info,
        _ => TagType.Danger
    };

    public string SizeText => FormatSize(Size);

    public string RetailText => IsRetailOnly
        ? StringRes.Get("Retail_RetailOnly")
        : StringRes.Get("Retail_General");

    public TagType RetailTagType => IsRetailOnly ? TagType.Warning : TagType.Success;

    public TagType ArchTagType => Architecture.ToLowerInvariant() switch
    {
        "x64" => TagType.Success,
        "arm64" => TagType.Danger,
        _ => TagType.Default,
    };

    public string Sha256Short => Sha256.Length > 24 ? $"{Sha256[..24]}..." : Sha256;

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
