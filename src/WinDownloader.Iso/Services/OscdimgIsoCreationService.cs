// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using WinDownloader.Iso.Interfaces;
using WinDownloader.Iso.Models;

namespace WinDownloader.Iso;

public sealed partial class OscdimgIsoCreationService : IIsoCreationService
{
    private readonly string _toolPath;

    public OscdimgIsoCreationService()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, "oscdimg.exe");
        _toolPath = File.Exists(candidate)
            ? candidate
            : throw new InvalidOperationException(
                "Can't find oscdimg.exe. Please place oscdimg.exe in the application's root directory.");
    }

    public async Task<IsoCreationResult> CreateIsoAsync(
        IsoCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warnings = new List<string>();
        string? outputDirectory = Path.GetDirectoryName(request.OutputIsoPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        List<string> arguments = CreateArguments(request, warnings);
        if (arguments.Count == 0)
        {
            return IsoCreationResult.Failure(
                request.OutputIsoPath,
                "staging 目录中没有可用的 UEFI 启动映像。",
                warnings);
        }

        if (File.Exists(request.OutputIsoPath))
        {
            File.Delete(request.OutputIsoPath);
        }

        var stopwatch = Stopwatch.StartNew();
        using Process process = StartProcess(_toolPath, arguments);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = ReadStdoutWithProgressAsync(process.StandardError, request.OnProgress, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { }
            throw;
        }

        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();

        bool succeeded = process.ExitCode == 0 && File.Exists(request.OutputIsoPath);
        long outputSize = succeeded ? new FileInfo(request.OutputIsoPath).Length : 0;

        return new IsoCreationResult(
            request.OutputIsoPath,
            succeeded,
            stopwatch.Elapsed,
            outputSize,
            _toolPath,
            string.Join(" ", arguments.Prepend(_toolPath).Select(static a => a.Contains(' ') ? $"\"{a}\"" : a)),
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

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 oscdimg.exe。");
    }

    private static List<string> CreateArguments(IsoCreationRequest request, List<string> warnings)
    {
        string uefiBootImage = Path.Combine(request.StagingDirectory, "efi", "microsoft", "boot", "efisys.bin");

        if (!File.Exists(uefiBootImage))
        {
            warnings.Add("未找到 efi\\microsoft\\boot\\efisys.bin，oscdimg 无法生成 UEFI 启动项。");
            return [];
        }

        string label = string.IsNullOrWhiteSpace(request.VolumeLabel) ? "ESD_ISO" : request.VolumeLabel.Trim();
        if (label.Length > 32)
        {
            label = label[..32];
        }

        var arguments = new List<string>
        {
            "-m",
            "-o",
            "-u1",
            "-udfver102",
            $"-l{label}",
            "-e",
            "-pEF",
            $"-b{uefiBootImage}",
            request.StagingDirectory,
            request.OutputIsoPath
        };
        return arguments;
    }

    /// <summary>
    /// oscdimg 用 \r 在同一行刷新进度，逐字符读取以便实时解析。
    /// </summary>
    private static async Task<string> ReadStdoutWithProgressAsync(
        StreamReader reader,
        Action<IsoOperationProgress>? onProgress,
        CancellationToken ct)
    {
        char[] buffer = new char[256];
        var segment = new StringBuilder();
        var all = new StringBuilder();

        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];
                if (c is '\r' or '\n')
                {
                    string line = segment.ToString().Trim();
                    if (line.Length > 0)
                    {
                        all.Append(line).Append('\n');
                        Match match = PercentageRegex().Match(line);
                        if (match.Success &&
                            double.TryParse(match.Groups[1].ValueSpan, out double pct))
                        {
                            onProgress?.Invoke(new IsoOperationProgress(pct));
                        }
                    }
                    segment.Clear();
                }
                else
                {
                    segment.Append(c);
                }
            }
        }

        string remaining = segment.ToString().Trim();
        if (remaining.Length > 0)
        {
            all.Append(remaining);
        }

        return all.ToString();
    }

    [GeneratedRegex(@"(\d+)%\s+complete", RegexOptions.IgnoreCase)]
    private static partial Regex PercentageRegex();
}
