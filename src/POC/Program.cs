using POC.Wim.Interfaces;
using POC.Wim.Models;
using POC.Wim.Services;

const string SourceEsdPath = @"C:\Users\kings\Downloads\WindowsImage\zh-cn\x64\26200.8246.260413-0654.25h2_ge_release_svc_refresh_CLIENTCONSUMER_RET_x64FRE_zh-cn.esd";

Console.WriteLine("WindowsImageDownloader POC");
Console.WriteLine("ESD to ISO image post-processing experiment.");
Console.WriteLine();

try
{
	var options = PocCommandLineOptions.Parse(args, SourceEsdPath);
	if (options.ShowHelp)
	{
		Console.WriteLine(PocCommandLineOptions.GetHelpText());
		return;
	}

	var request = options.CreateRequest();
	Console.WriteLine($"Source ESD: {request.SourceEsdPath}");
	Console.WriteLine($"Output root: {request.OutputRoot}");
	Console.WriteLine($"Install format: {request.InstallFormat}");
	Console.WriteLine($"ISO backend: {request.IsoBackend}");
	Console.WriteLine($"Volume label: {request.VolumeLabel}");
	Console.WriteLine();

	using var cancellation = new CancellationTokenSource();
	Console.CancelKeyPress += (_, eventArgs) =>
	{
		eventArgs.Cancel = true;
		cancellation.Cancel();
	};

	using var wimService = new WimProcessingService();
	var libraryInfo = await wimService.GetLibraryInfoAsync();
	var images = await wimService.GetImagesAsync(request.SourceEsdPath, cancellation.Token);

	Console.WriteLine($"wimlib: {libraryInfo.Version}");
	Console.WriteLine($"Images: {images.Count}");
	Console.WriteLine();
	PrintImages(images);

	if (options.InspectOnly)
	{
		Console.WriteLine("Inspect-only mode completed.");
		return;
	}

	Console.WriteLine();
	Console.WriteLine("Starting ESD -> ISO pipeline. Intermediate files will be kept.");
	Console.WriteLine();

	IIsoCreationService[] isoServices =
	{
		new OscdimgIsoCreationService(),
		new DiscUtilsIsoCreationService()
	};
	var pipeline = new EsdToIsoPipelineService(wimService, isoServices);
	var result = await pipeline.BuildAsync(request, new ConsoleProgressReporter(), cancellation.Token);

	Console.WriteLine();
	PrintResult(result);
}
catch (OperationCanceledException)
{
	Console.Error.WriteLine("Operation canceled.");
	Environment.ExitCode = 2;
}
catch (Exception ex)
{
	Console.Error.WriteLine(ex);
	Environment.ExitCode = 1;
}

static void PrintImages(IReadOnlyList<WimImageInfo> images)
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

static string FormatBytes(long bytes)
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

static void PrintResult(EsdToIsoResult result)
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
