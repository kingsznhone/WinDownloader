using System.Diagnostics;
using DiscUtils.Iso9660;
using POC.Wim.Interfaces;
using POC.Wim.Models;

namespace POC.Wim.Services;

public sealed class DiscUtilsIsoCreationService : IIsoCreationService
{
    private const long MaxIso9660FileSize = 4L * 1024 * 1024 * 1024 - 1;

    public IsoCreationBackend Backend => IsoCreationBackend.DiscUtils;

    public Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        IProgress<EsdToIsoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() => CreateIso(request, progress, cancellationToken), cancellationToken);
    }

    private static IsoCreationResult CreateIso(
        IsoCreationRequest request,
        IProgress<EsdToIsoProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var warnings = new List<string>
        {
            "DiscUtils 后端是实验性对照：输出不等价于 oscdimg 的 Windows ADK 双启动/UDF ISO。"
        };

        var outputDirectory = Path.GetDirectoryName(request.OutputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(request.OutputIsoPath))
        {
            File.Delete(request.OutputIsoPath);
        }

        var oversizedFiles = GetOversizedFiles(request.StagingDirectory, cancellationToken).ToList();
        if (oversizedFiles.Count > 0)
        {
            stopwatch.Stop();
            warnings.AddRange(oversizedFiles.Select(file => $"DiscUtils ISO9660/Joliet 后端不能安全写入超过 4 GiB 的单文件: {file}"));

            return IsoCreationResult.Skip(
                IsoCreationBackend.DiscUtils,
                request.OutputIsoPath,
                "DiscUtils 后端会截断超过 4 GiB 的单文件长度；请改用 oscdimg/UDF、install.esd 或 split WIM。",
                warnings);
        }

        try
        {
            progress?.Report(new EsdToIsoProgress(
                EsdToIsoStage.CreatingIso,
                "正在用 DiscUtils 创建实验 ISO",
                null,
                null,
                IsoCreationBackend.DiscUtils,
                request.OutputIsoPath,
                null,
                TimeSpan.Zero));

            var builder = new CDBuilder
            {
                UseJoliet = true,
                VolumeIdentifier = NormalizeVolumeLabel(request.VolumeLabel)
            };

            AddStagingFiles(builder, request.StagingDirectory, warnings, cancellationToken);

            using var bootImageStream = TryOpenUefiBootImage(request.StagingDirectory, warnings);
            if (bootImageStream is not null)
            {
                builder.SetBootImage(bootImageStream, BootDeviceEmulation.NoEmulation, 0);
            }

            builder.Build(request.OutputIsoPath);
            stopwatch.Stop();

            var outputSize = File.Exists(request.OutputIsoPath)
                ? new FileInfo(request.OutputIsoPath).Length
                : 0;

            return new IsoCreationResult(
                IsoCreationBackend.DiscUtils,
                request.OutputIsoPath,
                File.Exists(request.OutputIsoPath),
                Skipped: false,
                stopwatch.Elapsed,
                outputSize,
                ToolPath: null,
                CommandLine: null,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                warnings,
                File.Exists(request.OutputIsoPath) ? null : "DiscUtils 未生成 ISO 文件。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new IsoCreationResult(
                IsoCreationBackend.DiscUtils,
                request.OutputIsoPath,
                Succeeded: false,
                Skipped: false,
                stopwatch.Elapsed,
                OutputSize: 0,
                ToolPath: null,
                CommandLine: null,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                warnings,
                ex.Message);
        }
    }

    private static void AddStagingFiles(
        CDBuilder builder,
        string stagingDirectory,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AddDirectory(ToIsoPath(stagingDirectory, directory));
        }

        foreach (var filePath in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > MaxIso9660FileSize)
            {
                warnings.Add($"DiscUtils ISO9660/Joliet 后端不能安全写入超过 4 GiB 的单文件: {filePath}");
            }

            builder.AddFile(ToIsoPath(stagingDirectory, filePath), filePath);
        }
    }

    private static IEnumerable<string> GetOversizedFiles(string stagingDirectory, CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(filePath).Length > MaxIso9660FileSize)
            {
                yield return filePath;
            }
        }
    }

    private static FileStream? TryOpenUefiBootImage(string stagingDirectory, List<string> warnings)
    {
        var uefiBootImage = Path.Combine(stagingDirectory, "efi", "microsoft", "boot", "efisys.bin");
        if (File.Exists(uefiBootImage))
        {
            return File.OpenRead(uefiBootImage);
        }

        warnings.Add("未找到 efi\\microsoft\\boot\\efisys.bin，DiscUtils ISO 不会包含启动映像。");
        return null;
    }

    private static string ToIsoPath(string rootDirectory, string path)
    {
        return Path.GetRelativePath(rootDirectory, path).Replace(Path.DirectorySeparatorChar, '\\');
    }

    private static string NormalizeVolumeLabel(string volumeLabel)
    {
        var value = string.IsNullOrWhiteSpace(volumeLabel) ? "ESD_ISO" : volumeLabel.Trim();
        return value.Length <= 32 ? value : value[..32];
    }
}
