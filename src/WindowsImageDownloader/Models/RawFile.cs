namespace WindowsImageDownloader.Models;

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
        "%CLIENT%" => "消费者零售版",
        "%ENTERPRISE%" => "企业批量许可版",
        "%ENTERPRISE_N%" => "企业批量许可版 N",
        "%BASE_CHINA%" => "中国特供版",
        "" => "未分组",
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

    public string RetailText => IsRetailOnly ? "零售版" : "通用";

    public TagType RetailTagType => IsRetailOnly ? TagType.Warning : TagType.Success;

    public TagType ArchTagType => Architecture.ToLowerInvariant() switch
    {
        "x64"   => TagType.Success,
        "arm64" => TagType.Danger,
        _       => TagType.Default,
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
