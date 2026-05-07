using ManagedWimLib;
using WinDownloader.Interfaces;
using WinDownloader.Iso;
using WinDownloader.Iso.Interfaces;
using WinDownloader.Models;
using WinDownloader.Wim;

namespace WinDownloader.Services;

public sealed class EsdToIsoConversionService : IEsdToIsoConversionService
{
    private const uint BootChunkSize = 32 * 1024;
    private const uint InstallWimChunkSize = 128 * 1024;

    private readonly IIsoCreationService _isoCreationService;
    private readonly IWimProcessingService _wimProcessingService;

    public EsdToIsoConversionService(
        IWimProcessingService wimProcessingService,
        IIsoCreationService isoCreationService)
    {
        _wimProcessingService = wimProcessingService;
        _isoCreationService = isoCreationService;
    }

    public event EventHandler<EsdToIsoTaskSnapshot>? ProgressChanged;

    public async Task<EsdToIsoResult> ConvertAsync(
        EsdToIsoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var session = new ConversionSession(request, this, ProgressChanged);

        try
        {
            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.Preparing, 0, session.StagingDirectory, force: true);
            TryDeleteDirectory(session.StagingDirectory);
            Directory.CreateDirectory(session.StagingDirectory);
            Directory.CreateDirectory(session.SourcesDirectory);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.InspectingSource, 0.03, request.SourceEsdPath, force: true);
            session.Images = await _wimProcessingService.GetImagesAsync(request.SourceEsdPath, cancellationToken).ConfigureAwait(false);
            ValidateSourceImages(session.Images);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.ApplyingSetupMedia, 0.08, session.StagingDirectory, force: true);
            await _wimProcessingService.ExtractImageAsync(
                new WimExtractRequest(request.SourceEsdPath, 1, session.StagingDirectory),
                progress => session.PublishWimProgress(EsdToIsoStage.ApplyingSetupMedia, progress),
                cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.BuildingBootWim, 0.30, session.BootWimPath, force: true);
            await BuildBootWimAsync(request.SourceEsdPath, session, cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.BuildingInstallImage, 0.50, session.InstallWimPath, force: true);
            await BuildInstallWimAsync(request.SourceEsdPath, session, cancellationToken).ConfigureAwait(false);

            session.Publish(EsdToIsoTaskState.Running, EsdToIsoStage.CreatingIso, 0.86, session.IsoPath, force: true);
            session.IsoResult = await _isoCreationService.CreateIsoAsync(
                new IsoCreationRequest(session.StagingDirectory, session.IsoPath, request.VolumeLabel)
                {
                    OnProgress = p => session.Publish(
                        EsdToIsoTaskState.Running,
                        EsdToIsoStage.CreatingIso,
                        0.86d + 0.14d * p.Percent / 100d,
                        session.IsoPath,
                        isoProgress: p,
                        force: p.Percent >= 100d)
                },
                cancellationToken).ConfigureAwait(false);
            session.Warnings.AddRange(session.IsoResult.Warnings);

            if (!session.IsoResult.Succeeded)
                return session.Finish(false, session.IsoResult.ErrorMessage ?? "ISO Create failed.");

            return session.Finish(true, null);
        }
        catch (OperationCanceledException)
        {
            var completedAt = DateTimeOffset.Now;
            session.Publish(
                EsdToIsoTaskState.Canceled,
                session.CurrentStage,
                session.CurrentProgress,
                errorMessage: "Operation canceled.",
                completedAt: completedAt,
                force: true);
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                return session.Finish(false, ex.Message);
            }
            catch
            {
                session.Publish(EsdToIsoTaskState.Failed, EsdToIsoStage.Failed, session.CurrentProgress, errorMessage: ex.Message, force: true);
                throw;
            }
        }
        finally
        {
            if (!request.KeepIntermediateFiles)
                TryDeleteDirectory(session.StagingDirectory);
        }
    }

    private async Task BuildBootWimAsync(
        string sourceEsdPath,
        ConversionSession session,
        CancellationToken cancellationToken)
    {
        var items = session.Images
            .Where(static image => image.Index is 2 or 3)
            .Select(static image => CreateExportItem(image, image.Index == 3 ? ExportFlags.Boot : ExportFlags.None))
            .ToList();

        var request = new WimExportRequest(
            sourceEsdPath,
            session.BootWimPath,
            items,
            CompressionType.LZX,
            CheckIntegrity: true,
            Recompress: true,
            Solid: false,
            OutputChunkSize: BootChunkSize);

        await _wimProcessingService.ExportImagesAsync(
            request,
            progress => session.PublishWimProgress(EsdToIsoStage.BuildingBootWim, progress),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task BuildInstallWimAsync(
        string sourceEsdPath,
        ConversionSession session,
        CancellationToken cancellationToken)
    {
        var items = session.Images
            .Where(static image => image.Index >= 4)
            .Select(static image => CreateExportItem(image, ExportFlags.None))
            .ToList();

        var request = new WimExportRequest(
            sourceEsdPath,
            session.InstallWimPath,
            items,
            session.InstallCompression,
            CheckIntegrity: true,
            Recompress: session.RecompressInstallImage,
            Solid: true,
            OutputChunkSize: session.RecompressInstallImage ? InstallWimChunkSize : 0,
            OutputPackChunkSize: session.RecompressInstallImage ? InstallWimChunkSize : 0);

        await _wimProcessingService.ExportImagesAsync(
            request,
            progress => session.PublishWimProgress(EsdToIsoStage.BuildingInstallImage, progress),
            cancellationToken).ConfigureAwait(false);
    }

    private static WimImageExportItem CreateExportItem(WimImageInfo image, ExportFlags exportFlags)
    {
        var name = !string.IsNullOrWhiteSpace(image.Name) ? image.Name : image.Title;
        var description = !string.IsNullOrWhiteSpace(image.Description) ? image.Description : image.Title;
        return new WimImageExportItem(image.Index, name, description, exportFlags);
    }

    private static void ValidateRequest(EsdToIsoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceEsdPath))
        {
            throw new ArgumentException("Source ESD Path cannot be null or empty.", nameof(request));
        }

        if (!File.Exists(request.SourceEsdPath))
        {
            throw new FileNotFoundException($"Source ESD does not exist: {request.SourceEsdPath}", request.SourceEsdPath);
        }

        if (string.IsNullOrWhiteSpace(request.StagingDirectory))
        {
            throw new ArgumentException("Staging directory cannot be null or empty.", nameof(request));
        }

        if (!request.RecompressInstallImage && request.InstallCompression != CompressionType.LZMS)
        {
            throw new ArgumentException("Fast install.wim export keeps official solid LZMS resources; use LZMS compression or force recompression.", nameof(request));
        }
    }

    private static void ValidateSourceImages(IReadOnlyList<WimImageInfo> images)
    {
        if (images.Count < 4)
        {
            throw new InvalidOperationException("ESD must contain at least image 1, 2, 3, and one install image.");
        }

        foreach (var requiredIndex in new[] { 1, 2, 3, 4 })
        {
            if (images.All(image => image.Index != requiredIndex))
            {
                throw new InvalidOperationException($"ESD is missing required image index {requiredIndex}.");
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
