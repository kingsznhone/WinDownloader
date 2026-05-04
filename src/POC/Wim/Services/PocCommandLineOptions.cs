using POC.Wim.Models;

namespace POC.Wim.Services;

public sealed record PocCommandLineOptions(
    string SourceEsdPath,
    string? OutputRoot,
    InstallImageFormat InstallFormat,
    IsoCreationBackend IsoBackend,
    string VolumeLabel,
    bool InspectOnly,
    bool ShowHelp)
{
    public EsdToIsoRequest CreateRequest()
    {
        var sourcePath = Path.GetFullPath(SourceEsdPath);
        var outputRoot = string.IsNullOrWhiteSpace(OutputRoot)
            ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, "poc-iso-output")
            : Path.GetFullPath(OutputRoot);

        return new EsdToIsoRequest(
            sourcePath,
            outputRoot,
            InstallFormat,
            IsoBackend,
            VolumeLabel,
            KeepIntermediateFiles: true);
    }

    public static PocCommandLineOptions Parse(string[] args, string defaultSourceEsdPath)
    {
        var source = defaultSourceEsdPath;
        string? outputRoot = null;
        var installFormat = InstallImageFormat.Esd;
        var isoBackend = IsoCreationBackend.Both;
        var volumeLabel = "ESD_ISO";
        var inspectOnly = false;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;
                case "--source":
                    source = ReadValue(args, ref index, argument);
                    break;
                case "--output-root":
                    outputRoot = ReadValue(args, ref index, argument);
                    break;
                case "--install-format":
                    installFormat = ParseInstallFormat(ReadValue(args, ref index, argument));
                    break;
                case "--iso-backend":
                    isoBackend = ParseIsoBackend(ReadValue(args, ref index, argument));
                    break;
                case "--volume-label":
                    volumeLabel = ReadValue(args, ref index, argument);
                    break;
                case "--inspect-only":
                    inspectOnly = true;
                    break;
                default:
                    throw new ArgumentException($"未知参数: {argument}");
            }
        }

        return new PocCommandLineOptions(source, outputRoot, installFormat, isoBackend, volumeLabel, inspectOnly, showHelp);
    }

    public static string GetHelpText()
    {
        return """
        WindowsImageDownloader POC - ESD to ISO pipeline

        Options:
          --source <path>              Source ESD path. Defaults to the current hardcoded test ESD.
          --output-root <path>         Root folder for run outputs. Defaults beside the source ESD.
          --install-format <value>     esd, wim, or both. Default: esd.
          --iso-backend <value>        oscdimg, discutils, or both. Default: both.
          --volume-label <label>       ISO volume label. Default: ESD_ISO.
          --inspect-only               Only enumerate images and print the planned request.
          -h, --help                   Show this help.
        """;
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"参数 {option} 需要一个值。");
        }

        index++;
        return args[index];
    }

    private static InstallImageFormat ParseInstallFormat(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "esd" => InstallImageFormat.Esd,
            "wim" => InstallImageFormat.Wim,
            "both" => InstallImageFormat.Both,
            _ => throw new ArgumentException("--install-format 只能是 esd、wim 或 both。")
        };
    }

    private static IsoCreationBackend ParseIsoBackend(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "oscdimg" => IsoCreationBackend.Oscdimg,
            "discutils" => IsoCreationBackend.DiscUtils,
            "both" => IsoCreationBackend.Both,
            _ => throw new ArgumentException("--iso-backend 只能是 oscdimg、discutils 或 both。")
        };
    }
}
