using System.CommandLine;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using POC.Models;
using POC.Services;

namespace POC;

internal static class Program
{
    private const string DefaultSourceEsdPath =
        @"C:\Users\kings\Downloads\WindowsImage\zh-cn\x64\26200.8246.260413-0654.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_zh-cn.esd";

    public static async Task<int> Main(string[] args)
    {
        using var wimService = new WimProcessingService();
        var images = await wimService.GetImagesAsync(DefaultSourceEsdPath).ConfigureAwait(false);

        Console.WriteLine($"Found {images.Count} image(s) in: {Path.GetFileName(DefaultSourceEsdPath)}");
        Console.WriteLine();
        foreach (var img in images)
        {
            Console.WriteLine($"  [{img.Index,2}] {img.Title,-30}  {img.Subtitle}");
        }
        Console.WriteLine();

        var outputRoot = Path.Combine(Path.GetDirectoryName(DefaultSourceEsdPath)!, "extracted");
        Directory.CreateDirectory(outputRoot);

        images = images.Take(3).ToList();
        foreach (var image in images)
        {
            var destDir = Path.Combine(outputRoot, $"{image.Index}");
            var prefix = $"[{image.Index}/{images.Count}] {image.Title}: ";

            var progressRow = Console.CursorTop;
            Console.Write(prefix);

            await wimService.ExtractImageAsync(
                new WimExtractRequest(DefaultSourceEsdPath, image.Index, destDir),
                progress =>
                {
                        PrintWimProgress(progressRow, progress);
                },
                CancellationToken.None).ConfigureAwait(false);

            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine($"All images extracted to: {outputRoot}");
        return 0;

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

        var keepIntermediateOption = new Option<bool>("--keep-intermediate")
        {
            Description = "Keep staging files after conversion. This is the default."
        };

        var deleteIntermediateOption = new Option<bool>("--delete-intermediate")
        {
            Description = "Delete staging files after a successful conversion."
        };

        var rootCommand = new RootCommand("WindowsImageDownloader POC - ESD to ISO conversion service");
        rootCommand.Add(sourceOption);
        rootCommand.Add(outputRootOption);
        rootCommand.Add(volumeLabelOption);
        rootCommand.Add(keepIntermediateOption);
        rootCommand.Add(deleteIntermediateOption);

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var outputRoot = parseResult.GetValue(outputRootOption);
            var volumeLabel = parseResult.GetValue(volumeLabelOption)!;
            var keepIntermediate = parseResult.GetValue(keepIntermediateOption);
            var deleteIntermediate = parseResult.GetValue(deleteIntermediateOption);
            var keepIntermediateFiles = keepIntermediate || !deleteIntermediate;

            var sourcePath = Path.GetFullPath(source);
            var resolvedOutputRoot = string.IsNullOrWhiteSpace(outputRoot)
                ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, "poc-iso-output")
                : Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(resolvedOutputRoot);

            var consoleLogPath = Path.Combine(
                resolvedOutputRoot,
                $"console-{Path.GetFileNameWithoutExtension(sourcePath)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");

            var request = new EsdToIsoRequest(
                sourcePath,
                resolvedOutputRoot,
                volumeLabel,
                keepIntermediateFiles);

            using var consoleLog = ConsoleLogScope.Start(consoleLogPath);

            Console.WriteLine("WindowsImageDownloader POC");
            Console.WriteLine("ESD to ISO conversion service experiment.");
            Console.WriteLine();
            Console.WriteLine($"Source ESD: {request.SourceEsdPath}");
            Console.WriteLine($"Output root: {request.OutputRoot}");
            Console.WriteLine($"Console log: {consoleLogPath}");
            Console.WriteLine($"Volume label: {request.VolumeLabel}");
            Console.WriteLine($"Keep intermediate files: {request.KeepIntermediateFiles}");
            Console.WriteLine();

            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                linkedCancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
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
        Console.WriteLine($"Run directory: {result.RunDirectory}");
        Console.WriteLine($"Staging: {result.StagingDirectory}");
        Console.WriteLine($"boot.wim: {result.BootWimPath}");
        Console.WriteLine($"install.esd: {result.InstallEsdPath}");
        Console.WriteLine($"ISO: {result.IsoPath}");

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            Console.WriteLine($"Error: {result.ErrorMessage}");
        }

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"Warning: {warning}");
        }
    }

    private static void OnProgressChanged(object? sender, EsdToIsoTaskSnapshot snapshot)
    {
        Console.WriteLine(FormatSnapshot(snapshot));
    }

    private static string FormatSnapshot(EsdToIsoTaskSnapshot snapshot)
    {
        var percent = $"{snapshot.Progress * 100,5:0.0}%";
        var elapsed = snapshot.Elapsed.ToString(@"hh\:mm\:ss");
        var file = string.IsNullOrWhiteSpace(snapshot.CurrentFile) ? string.Empty : $" ({Path.GetFileName(snapshot.CurrentFile)})";
        var nested = snapshot.WimProgress?.Percent is null ? string.Empty : $" WIM {snapshot.WimProgress.Percent.Value:0.0}%";
        return $"{elapsed} {percent} {snapshot.State}/{snapshot.Stage}: {snapshot.StatusText}{nested}{file}";
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
