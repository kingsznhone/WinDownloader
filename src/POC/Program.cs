using System.CommandLine;
using POC.Interfaces;
using POC.Models;
using POC.Services;

namespace POC;

internal static class Program
{
    private const string DefaultSourceEsdPath =
        @"C:\Users\kings\Downloads\WindowsImage\zh-cn\x64\26200.8246.260413-0654.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_zh-cn.esd";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            args = ["--help"];

        var sourceOption = new Option<string>("--source")
        {
            Description = "Source ESD path. Defaults to the current hardcoded test ESD.",
            DefaultValueFactory = _ => DefaultSourceEsdPath
        };

        var outputRootOption = new Option<string?>("--output-root")
        {
            Description = "Root folder for run outputs. Defaults beside the source ESD."
        };

        var installFormatOption = new Option<InstallImageFormat>("--install-format")
        {
            Description = "Install image format: esd or wim.",
            DefaultValueFactory = _ => InstallImageFormat.Esd
        };

        var volumeLabelOption = new Option<string>("--volume-label")
        {
            Description = "ISO volume label.",
            DefaultValueFactory = _ => "ESD_ISO"
        };

        var inspectOnlyOption = new Option<bool>("--inspect-only")
        {
            Description = "Only enumerate images and print the planned request, skip the pipeline."
        };

        var rootCommand = new RootCommand("WindowsImageDownloader POC - ESD to ISO pipeline");
        rootCommand.Add(sourceOption);
        rootCommand.Add(outputRootOption);
        rootCommand.Add(installFormatOption);
        rootCommand.Add(volumeLabelOption);
        rootCommand.Add(inspectOnlyOption);

        rootCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var source = parseResult.GetValue(sourceOption)!;
            var outputRoot = parseResult.GetValue(outputRootOption);
            var installFormat = parseResult.GetValue(installFormatOption);
            var volumeLabel = parseResult.GetValue(volumeLabelOption)!;
            var inspectOnly = parseResult.GetValue(inspectOnlyOption);

            var sourcePath = Path.GetFullPath(source);
            var resolvedOutputRoot = string.IsNullOrWhiteSpace(outputRoot)
                ? Path.Combine(Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory, "poc-iso-output")
                : Path.GetFullPath(outputRoot);

            var request = new EsdToIsoRequest(
                sourcePath,
                resolvedOutputRoot,
                installFormat,
                IsoCreationBackend.Oscdimg,
                volumeLabel,
                KeepIntermediateFiles: true);

            Console.WriteLine("WindowsImageDownloader POC");
            Console.WriteLine("ESD to ISO image post-processing experiment.");
            Console.WriteLine();
            Console.WriteLine($"Source ESD: {request.SourceEsdPath}");
            Console.WriteLine($"Output root: {request.OutputRoot}");
            Console.WriteLine($"Install format: {request.InstallFormat}");
            Console.WriteLine($"Volume label: {request.VolumeLabel}");
            Console.WriteLine();

            try
            {
                using var wimService = new WimProcessingService();
                var libraryInfo = await wimService.GetLibraryInfoAsync(ct);
                var images = await wimService.GetImagesAsync(request.SourceEsdPath, ct);

                Console.WriteLine($"wimlib: {libraryInfo.Version}");
                Console.WriteLine($"Images: {images.Count}");
                Console.WriteLine();
                PrintImages(images);

                if (inspectOnly)
                {
                    Console.WriteLine("Inspect-only mode completed.");
                    return 0;
                }

                Console.WriteLine();
                Console.WriteLine("Starting ESD -> ISO pipeline. Intermediate files will be kept.");
                Console.WriteLine();

                var pipeline = new EsdToIsoPipelineService(wimService, new OscdimgIsoCreationService());
                var result = await pipeline.BuildAsync(request, new ConsoleProgressReporter(), ct);

                Console.WriteLine();
                PrintResult(result);
                return 0;
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
        });

        return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
    }

    private static void PrintImages(IReadOnlyList<WimImageInfo> images)
    {
        foreach (var image in images)
        {
            Console.WriteLine($"[{image.Index}] {image.Title}");

            if (!string.IsNullOrWhiteSpace(image.Subtitle))
            {
                Console.WriteLine($"    {image.Subtitle}");
            }

            if (image.TotalBytes > 0)
            {
                Console.WriteLine($"    Size: {FormatBytes(image.TotalBytes)}");
            }

            if (image.IsBootable)
            {
                Console.WriteLine("    Bootable: true");
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = (double)bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static void PrintResult(EsdToIsoResult result)
    {
        Console.WriteLine("Completed.");
        Console.WriteLine($"Run directory: {result.RunDirectory}");
        Console.WriteLine($"Staging: {result.StagingDirectory}");
        Console.WriteLine($"boot.wim: {result.BootWimPath}");

        foreach (var installImagePath in result.InstallImagePaths)
        {
            Console.WriteLine($"Install image: {installImagePath}");
        }

        foreach (var isoResult in result.IsoResults)
        {
            var state = isoResult.Succeeded ? "succeeded" : isoResult.Skipped ? "skipped" : "failed";
            Console.WriteLine($"ISO {isoResult.Backend}: {state} -> {isoResult.OutputIsoPath}");
            if (!string.IsNullOrWhiteSpace(isoResult.ErrorMessage))
            {
                Console.WriteLine($"    {isoResult.ErrorMessage}");
            }
        }

        Console.WriteLine($"Manifest: {result.ManifestPath}");
        Console.WriteLine($"Events: {result.EventsPath}");
        Console.WriteLine($"Summary: {result.SummaryPath}");
    }
}
