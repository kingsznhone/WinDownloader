using System.CommandLine;
using System.Diagnostics;
using System.Text;
using ManagedWimLib;
using WinDownloader.Iso;
using WinDownloader.Wim;

namespace POC;

internal static class Program
{
    private const string DefaultSourceEsdPath =
        @"C:\Users\kings\Downloads\WindowsImage\zh-cn\x64\26200.8246.260413-0654.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_zh-cn.esd";

    public static async Task<int> Main(string[] args)
    {
        var sourceOption = new Option<string>("--source")
        {
            Description = "Source ESD path. Defaults to the current hardcoded test ESD.",
            DefaultValueFactory = _ => DefaultSourceEsdPath
        };

        var outputRootOption = new Option<string?>("--output-root")
        {
            Description = "Root folder for run outputs. Defaults beside the source ESD."
        };

        var volumeLabelOption = new Option<string>("--volume-label")
        {
            Description = "ISO volume label.",
            DefaultValueFactory = _ => "ESD_ISO"
        };

        var deleteIntermediateOption = new Option<bool>("--delete-staging")
        {
            Description = "Delete staging files after a successful conversion. Defaults to keeping them."
        };

        var installCompressionOption = new Option<CompressionType>("--install-compression")
        {
            Description = "Compression algorithm for install.wim. LZMS is the default fast path; LZX forces recompression.",
            DefaultValueFactory = _ => CompressionType.LZMS
        };

        var reuseInstallResourcesOption = new Option<bool>("--reuse-install-resources")
        {
            Description = "Reuse official solid LZMS ESD resources when building install.wim. This is the default.",
            DefaultValueFactory = _ => true
        };

        var recompressInstallImageOption = new Option<bool>("--recompress-install-image")
        {
            Description = "Force install.wim recompression for benchmarking or alternate compression choices."
        };

        var isoOnlyOption = new Option<bool>("--iso-only")
        {
            Description = "Skip WIM/ESD building stages and only run ISO packaging on the existing staging directory. Useful for testing ISO progress reporting."
        };

        var rootCommand = new RootCommand("WindowsImageDownloader POC - ESD to ISO conversion service")
        {
            sourceOption,
            outputRootOption,
            volumeLabelOption,
            deleteIntermediateOption,
            installCompressionOption,
            reuseInstallResourcesOption,
            recompressInstallImageOption,
            isoOnlyOption
        };

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var outputRoot = parseResult.GetValue(outputRootOption);
            var volumeLabel = parseResult.GetValue(volumeLabelOption)!;
            var keepIntermediateFiles = !parseResult.GetValue(deleteIntermediateOption);
            var installCompression = parseResult.GetValue(installCompressionOption);
            var recompressInstallImage = parseResult.GetValue(recompressInstallImageOption) ||
                installCompression != CompressionType.LZMS ||
                !parseResult.GetValue(reuseInstallResourcesOption);
            var reuseInstallResources = !recompressInstallImage;
            var isoOnly = parseResult.GetValue(isoOnlyOption);

            var sourcePath = Path.GetFullPath(source);
            var resolvedStagingRoot = string.IsNullOrWhiteSpace(outputRoot)
                ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, "poc-iso-staging")
                : Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(resolvedStagingRoot);

            var consoleLogPath = Path.Combine(
                resolvedStagingRoot,
                $"console-{Path.GetFileNameWithoutExtension(sourcePath)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

            using var consoleLog = ConsoleLogScope.Start(consoleLogPath);

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                linkedCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                if (isoOnly)
                {
                    var stagingDirectory = Path.Combine(resolvedStagingRoot, "staging");
                    var isoPath = Path.Combine(
                        Path.GetDirectoryName(sourcePath)!,
                        Path.GetFileNameWithoutExtension(sourcePath) + ".iso");

                    Console.WriteLine("WindowsImageDownloader POC");
                    Console.WriteLine("ISO-only mode: skipping WIM/ESD stages.");
                    Console.WriteLine();
                    Console.WriteLine($"Staging directory: {stagingDirectory}");
                    Console.WriteLine($"ISO output: {isoPath}");
                    Console.WriteLine($"Volume label: {volumeLabel}");
                    Console.WriteLine($"Console log: {consoleLogPath}");
                    Console.WriteLine();

                    var stopwatch = Stopwatch.StartNew();
                    var isoService = new OscdimgIsoCreationService();
                    var isoRequest = new IsoCreationRequest(stagingDirectory, isoPath, volumeLabel)
                    {
                        OnProgress = p =>
                        {
                            var elapsed = stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                            Console.WriteLine($"{elapsed} {p.Percent,5:0.0}% CreatingIso");
                        }
                    };

                    var isoResult = await isoService.CreateIsoAsync(isoRequest, linkedCancellation.Token).ConfigureAwait(false);
                    stopwatch.Stop();

                    Console.WriteLine();
                    Console.WriteLine(isoResult.Succeeded ? "Completed." : "Failed.");
                    Console.WriteLine($"ISO: {isoResult.OutputIsoPath}");
                    Console.WriteLine($"Duration: {isoResult.Duration}");
                    if (!isoResult.Succeeded)
                        Console.Error.WriteLine($"Error: {isoResult.ErrorMessage}");
                    foreach (var w in isoResult.Warnings)
                        Console.WriteLine($"Warning: {w}");

                    // Diagnostic: dump raw oscdimg stdout to reveal control characters
                    var raw = isoResult.StandardOutput;
                    var rawErr = isoResult.StandardError;
                    Console.WriteLine();
                    Console.WriteLine($"--- oscdimg stdout ({raw.Length} chars) ---");
                    Console.WriteLine(raw.Replace("\b", "[BS]").Replace("\r", "[CR]").Replace("\n", "[LF]\n"));
                    Console.WriteLine($"--- oscdimg stderr ({rawErr.Length} chars) ---");
                    Console.WriteLine(rawErr.Replace("\b", "[BS]").Replace("\r", "[CR]").Replace("\n", "[LF]\n"));
                    Console.WriteLine();
                    Console.WriteLine("--- hex dump stdout (first 256 chars) ---");
                    for (int i = 0; i < Math.Min(raw.Length, 256); i++)
                    {
                        Console.Write($"{(int)raw[i]:X2} ");
                        if ((i + 1) % 16 == 0) Console.WriteLine();
                    }
                    Console.WriteLine();
                    Console.WriteLine("--- hex dump stderr (first 256 chars) ---");
                    for (int i = 0; i < Math.Min(rawErr.Length, 256); i++)
                    {
                        Console.Write($"{(int)rawErr[i]:X2} ");
                        if ((i + 1) % 16 == 0) Console.WriteLine();
                    }
                    Console.WriteLine();

                    return isoResult.Succeeded ? 0 : 1;
                }

                var request = new EsdToIsoRequest(
                    sourcePath,
                    Path.Combine(resolvedStagingRoot, "staging"),
                    volumeLabel,
                    keepIntermediateFiles,
                    installCompression,
                    RecompressInstallImage: recompressInstallImage);

                Console.WriteLine("WindowsImageDownloader POC");
                Console.WriteLine($"Source ESD: {sourcePath}");
                Console.WriteLine($"Output root: {resolvedStagingRoot}");
                Console.WriteLine($"Install export: {(reuseInstallResources ? "reuse official solid LZMS resources into install.wim" : "recompress install.wim")}");
                Console.WriteLine($"Install compression: {installCompression}");
                Console.WriteLine($"Console log: {consoleLogPath}");
                Console.WriteLine();

                using var wimService = new WimProcessingService();
                var conversionService = new EsdToIsoConversionService(wimService, new OscdimgIsoCreationService());
                conversionService.ProgressChanged += OnProgressChanged;

                var result = await conversionService.ConvertAsync(request, linkedCancellation.Token).ConfigureAwait(false);

                conversionService.ProgressChanged -= OnProgressChanged;
                Console.WriteLine();
                PrintResult(result);
                return result.Succeeded ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Operation canceled.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        });

        return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
    }

    private static void PrintWimProgress(int row, WimOperationProgress progress)
    {
        var stage = $"[{progress.Stage}]";
        var percent = progress.Percent.HasValue ? $"{progress.Percent.Value,5:0.0}%" : "      ";
        var bytes = progress.TotalBytes is > 0
            ? $"  {FormatBytes(progress.CompletedBytes ?? 0),10} / {FormatBytes(progress.TotalBytes.Value),-10}"
            : string.Empty;
        var item = !string.IsNullOrWhiteSpace(progress.CurrentItem)
            ? $"  {Path.GetFileName(progress.CurrentItem)}"
            : string.Empty;
        var suffix = $"{stage,-12} {percent}{bytes}{item}";
        Console.SetCursorPosition(0, row);
        Console.Write(suffix.PadRight(Console.WindowWidth - 1));
    }

    private static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.00} MB",
            >= 1024 => $"{bytes / 1024.0:0.0} KB",
            _ => $"{bytes} B"
        };
    }

    private static void PrintResult(EsdToIsoResult result)
    {
        Console.WriteLine(result.Succeeded ? "Completed." : "Failed.");
        Console.WriteLine($"Staging: {result.StagingDirectory}");
        Console.WriteLine($"boot.wim: {result.BootWimPath}");
        Console.WriteLine($"install.wim: {result.InstallWimPath}");
        Console.WriteLine($"ISO: {result.IsoPath}");
        Console.WriteLine($"Duration: {result.Duration}");
        PrintFileSize("boot.wim", result.BootWimPath);
        PrintFileSize("install.wim", result.InstallWimPath);
        PrintFileSize("ISO", result.IsoPath);

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            Console.WriteLine($"Error: {result.ErrorMessage}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }
    }

    private static void PrintFileSize(string label, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Console.WriteLine($"{label} size: {FormatBytes((ulong)new FileInfo(path).Length)}");
    }

    private static void OnProgressChanged(object? sender, EsdToIsoTaskSnapshot snapshot)
    {
        Console.WriteLine(FormatSnapshot(snapshot));
    }

    private static string FormatSnapshot(EsdToIsoTaskSnapshot snapshot)
    {
        var percent = $"{snapshot.Progress * 100,5:0.0}%";
        var elapsed = snapshot.Elapsed.ToString(@"hh\:mm\:ss");
        var details = BuildSnapshotDetails(snapshot);
        return $"{elapsed} {percent} {snapshot.State}/{snapshot.Stage}: {details}";
    }

    private static string BuildSnapshotDetails(EsdToIsoTaskSnapshot snapshot)
    {
        var file = !string.IsNullOrWhiteSpace(snapshot.CurrentFile)
            ? $" ({Path.GetFileName(snapshot.CurrentFile)})"
            : string.Empty;

        if (snapshot.WimProgress is { } wim)
        {
            return wim.Stage switch
            {
                WimOperationStage.Extracting => wim.Percent.HasValue
                    ? $"正在提取映像 WIM {wim.Percent.Value:0.0}%{file}"
                    : $"正在提取映像{file}",
                WimOperationStage.Writing => wim.Percent.HasValue
                    ? $"正在写入映像 WIM {wim.Percent.Value:0.0}% ({wim.CurrentItem})"
                    : $"正在写入映像{file}",
                WimOperationStage.Verifying => wim.Percent.HasValue
                    ? $"正在校验数据流 WIM {wim.Percent.Value:0.0}%"
                    : $"正在校验数据流{file}",
                WimOperationStage.Metadata => !string.IsNullOrWhiteSpace(wim.CurrentItem)
                    ? $"正在处理元数据 ({wim.CurrentItem})"
                    : "正在处理元数据",
                WimOperationStage.Completed => $"完成 WIM 100.0%{file}",
                _ => $"{wim.Stage}{file}"
            };
        }

        return snapshot.Stage switch
        {
            EsdToIsoStage.Preparing => $"正在清理并准备 staging 目录{file}",
            EsdToIsoStage.InspectingSource => $"正在读取 ESD 映像信息{file}",
            EsdToIsoStage.ApplyingSetupMedia => $"正在展开 image 1 到 ISO staging{file}",
            EsdToIsoStage.BuildingBootWim => $"正在生成 boot.wim{file}",
            EsdToIsoStage.BuildingInstallImage => $"正在生成 install.wim{file}",
            EsdToIsoStage.CreatingIso => snapshot.IsoProgress is not null
                ? $"正在创建 ISO {snapshot.IsoProgress.Percent:0}%{file}"
                : $"正在调用 oscdimg 创建 ISO{file}",
            EsdToIsoStage.Completed => $"ESD 到 ISO 转换完成{file}",
            EsdToIsoStage.Failed => !string.IsNullOrWhiteSpace(snapshot.ErrorMessage)
                ? $"ESD 到 ISO 转换失败: {snapshot.ErrorMessage}"
                : "ESD 到 ISO 转换失败",
            _ => $"{snapshot.Stage}{file}"
        };
    }

    private sealed class ConsoleLogScope : IDisposable
    {
        private readonly TextWriter _originalError;
        private readonly TextWriter _originalOutput;
        private readonly TextWriter _logWriter;

        private ConsoleLogScope(TextWriter originalOutput, TextWriter originalError, TextWriter logWriter)
        {
            _originalOutput = originalOutput;
            _originalError = originalError;
            _logWriter = logWriter;
        }

        public static ConsoleLogScope Start(string path)
        {
            var originalOutput = Console.Out;
            var originalError = Console.Error;
            var streamWriter = TextWriter.Synchronized(new StreamWriter(path, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            });

            Console.SetOut(new TeeTextWriter(originalOutput, streamWriter));
            Console.SetError(new TeeTextWriter(originalError, streamWriter));
            return new ConsoleLogScope(originalOutput, originalError, streamWriter);
        }

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            Console.SetError(_originalError);
            _logWriter.Dispose();
        }
    }

    private sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _first;
        private readonly TextWriter _second;

        public TeeTextWriter(TextWriter first, TextWriter second)
        {
            _first = first;
            _second = second;
        }

        public override Encoding Encoding => _first.Encoding;

        public override void Flush()
        {
            _first.Flush();
            _second.Flush();
        }

        public override void Write(char value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void Write(string? value)
        {
            _first.Write(value);
            _second.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _first.WriteLine(value);
            _second.WriteLine(value);
        }
    }
}
