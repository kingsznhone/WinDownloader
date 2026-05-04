using POC.Models;

namespace POC.Services;

internal static class EsdToIsoPipelinePlanning
{
    public const uint BootChunkSize = 32 * 1024;
    public const uint InstallEsdChunkSize = 128 * 1024;
    private const long FourGiB = 4L * 1024 * 1024 * 1024;

    public static EsdToIsoRunPaths CreateRunPaths(EsdToIsoRequest request)
    {
        var sourceName = Path.GetFileNameWithoutExtension(request.SourceEsdPath);
        var runName = $"{sourceName}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
        var runDirectory = Path.Combine(request.OutputRoot, runName);

        return new EsdToIsoRunPaths(
            runDirectory,
            Path.Combine(runDirectory, "staging"),
            Path.Combine(runDirectory, "events.ndjson"),
            Path.Combine(runDirectory, "manifest.json"),
            Path.Combine(runDirectory, "summary.txt"));
    }

    public static IEnumerable<IsoCreationBackend> ExpandBackends(IsoCreationBackend backend)
    {
        return [backend];
    }

    public static InstallTarget GetInstallTarget(InstallImageFormat format, string sourcesDirectory)
    {
        return format switch
        {
            InstallImageFormat.Wim => new InstallTarget(
                Path.Combine(sourcesDirectory, "install.wim"),
                WimCompressionKind.LZX,
                Solid: false,
                BootChunkSize,
                PackChunkSize: 0),
            _ => new InstallTarget(
                Path.Combine(sourcesDirectory, "install.esd"),
                WimCompressionKind.LZMS,
                Solid: true,
                InstallEsdChunkSize,
                InstallEsdChunkSize)
        };
    }

    public static WimImageExportItem CreateExportItem(WimImageInfo image, bool markBootable)
    {
        var name = !string.IsNullOrWhiteSpace(image.Name) ? image.Name : image.Title;
        var description = !string.IsNullOrWhiteSpace(image.Description) ? image.Description : image.Title;
        return new WimImageExportItem(image.Index, name, description, markBootable);
    }

    public static void AddFileSizeWarnings(string path, List<string> warnings)
    {
        if (!File.Exists(path))
        {
            warnings.Add($"未找到预期输出文件: {path}");
            return;
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > FourGiB)
        {
            warnings.Add($"{Path.GetFileName(path)} 超过 4 GiB，ISO 后端和目标启动方式需要重点验证。");
        }
    }

    public static void ValidateRequest(EsdToIsoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEsdPath))
        {
            throw new ArgumentException("源 ESD 路径不能为空。", nameof(request));
        }

        if (!File.Exists(request.SourceEsdPath))
        {
            throw new FileNotFoundException($"源 ESD 不存在: {request.SourceEsdPath}", request.SourceEsdPath);
        }

        if (string.IsNullOrWhiteSpace(request.OutputRoot))
        {
            throw new ArgumentException("输出根目录不能为空。", nameof(request));
        }
    }

    public static void ValidateSourceImages(IReadOnlyList<WimImageInfo> images)
    {
        if (images.Count < 4)
        {
            throw new InvalidOperationException("ESD 至少需要包含 image 1、2、3 和一个安装映像。");
        }

        foreach (var requiredIndex in new[] { 1, 2, 3, 4 })
        {
            if (images.All(image => image.Index != requiredIndex))
            {
                throw new InvalidOperationException($"ESD 缺少必需映像 index {requiredIndex}。");
            }
        }
    }
}
