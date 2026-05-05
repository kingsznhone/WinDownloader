using System.Diagnostics;
using POC.Interfaces;
using POC.Models;

namespace POC.Services;

public sealed class OscdimgIsoCreationService : IIsoCreationService
{
    public async Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var warnings = new List<string>();
        var outputDirectory = Path.GetDirectoryName(request.OutputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var toolPath = FindOscdimg();
        if (toolPath is null)
        {
            return IsoCreationResult.Failure(
                request.OutputIsoPath,
                "未找到 oscdimg.exe。请安装 Windows ADK，或将 oscdimg.exe 放入 POC 输出目录。");
        }

        var arguments = CreateArguments(request, Path.GetDirectoryName(toolPath), warnings);
        if (arguments.Count == 0)
        {
            return IsoCreationResult.Failure(
                request.OutputIsoPath,
                "staging 目录中没有可用的 BIOS/UEFI 启动映像。",
                warnings);
        }

        if (File.Exists(request.OutputIsoPath))
        {
            File.Delete(request.OutputIsoPath);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = StartProcess(toolPath, arguments);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();

        var succeeded = process.ExitCode == 0 && File.Exists(request.OutputIsoPath);
        var outputSize = succeeded ? new FileInfo(request.OutputIsoPath).Length : 0;

        return new IsoCreationResult(
            request.OutputIsoPath,
            succeeded,
            stopwatch.Elapsed,
            outputSize,
            toolPath,
            FormatCommandLine(toolPath, arguments),
            process.ExitCode,
            stdout,
            stderr,
            warnings,
            succeeded ? null : $"oscdimg 失败，退出码 {process.ExitCode}。");
    }

    private static Process StartProcess(string toolPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(toolPath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 oscdimg.exe。");
    }

    private static List<string> CreateArguments(IsoCreationRequest request, string? toolDirectory, List<string> warnings)
    {
        var biosBootImage = ResolveBootImage(toolDirectory, request.StagingDirectory, "etfsboot.com", Path.Combine("boot", "etfsboot.com"));
        var uefiBootImage = ResolveBootImage(toolDirectory, request.StagingDirectory, "efisys.bin", Path.Combine("efi", "microsoft", "boot", "efisys.bin"));
        var hasBiosBoot = File.Exists(biosBootImage);
        var hasUefiBoot = File.Exists(uefiBootImage);

        if (!hasBiosBoot)
        {
            warnings.Add("未找到 boot\\etfsboot.com，oscdimg 无法生成 BIOS 启动项。");
        }

        if (!hasUefiBoot)
        {
            warnings.Add("未找到 efi\\microsoft\\boot\\efisys.bin，oscdimg 无法生成 UEFI 启动项。");
        }

        if (!hasBiosBoot && !hasUefiBoot)
        {
            return [];
        }

        var arguments = new List<string>
        {
            "-m",
            "-o",
            "-u2",
            "-udfver102",
            $"-l{NormalizeVolumeLabel(request.VolumeLabel)}"
        };

        if (hasBiosBoot && hasUefiBoot)
        {
            arguments.Add($"-bootdata:2#p0,e,b{biosBootImage}#pEF,e,b{uefiBootImage}");
        }
        else if (hasBiosBoot)
        {
            arguments.Add($"-b{biosBootImage}");
        }
        else
        {
            arguments.Add($"-bootdata:1#pEF,e,b{uefiBootImage}");
        }

        arguments.Add(request.StagingDirectory);
        arguments.Add(request.OutputIsoPath);
        return arguments;
    }

    private static string? FindOscdimg()
    {
        foreach (var candidate in GetBundledOscdimgPaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "oscdimg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var candidate in GetCommonOscdimgPaths())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetBundledOscdimgPaths()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "Oscdimg", "oscdimg.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "oscdimg.exe");
    }

    private static string ResolveBootImage(string? toolDirectory, string stagingDirectory, string toolFileName, string stagingRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(toolDirectory))
        {
            var bundled = Path.Combine(toolDirectory, toolFileName);
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        return Path.Combine(stagingDirectory, stagingRelativePath);
    }

    private static IEnumerable<string> GetCommonOscdimgPaths()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var suffixes = new[]
        {
            Path.Combine("Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "amd64", "Oscdimg", "oscdimg.exe"),
            Path.Combine("Windows Kits", "10", "Assessment and Deployment Kit", "Deployment Tools", "x86", "Oscdimg", "oscdimg.exe")
        };

        foreach (var root in new[] { programFilesX86, programFiles }.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var suffix in suffixes)
            {
                yield return Path.Combine(root, suffix);
            }
        }
    }

    private static string NormalizeVolumeLabel(string volumeLabel)
    {
        var value = string.IsNullOrWhiteSpace(volumeLabel) ? "ESD_ISO" : volumeLabel.Trim();
        return value.Length <= 32 ? value : value[..32];
    }

    private static string FormatCommandLine(string toolPath, IEnumerable<string> arguments)
    {
        return string.Join(" ", new[] { Quote(toolPath) }.Concat(arguments.Select(Quote)));
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
