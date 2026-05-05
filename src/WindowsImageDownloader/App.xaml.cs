using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using WindowsImageDownloader.Interfaces;
using WindowsImageDownloader.Services;
using WindowsImageDownloader.ViewModels;

namespace WindowsImageDownloader;

public partial class App : Application
{
    private Window? _window;
    private IHost? _host;

    public App()
    {
        InitializeComponent();
        _host = BuildHost();
    }

    public static IServiceProvider Services => ((App)Current)._host!.Services;
    public static Window MainWindow { get; private set; } = default!;
    public static T GetService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IAppSettings, AppSettingsService>(static _ =>
        {
            var settings = new AppSettingsService();
            settings.EnsureDefaults();
            return settings;
        });
        builder.Services.AddSingleton<IUpdateCatalogService, UpdateCatalogService>();
        builder.Services.AddSingleton<ICacheService, CacheService>();
        builder.Services.AddSingleton<IDownloadService, DownloadService>();
        builder.Services.AddSingleton<IDownloadTaskPathService, DownloadTaskPathService>();
        builder.Services.AddSingleton<IEsdDownloadPipeline, EsdDownloadPipeline>();
        builder.Services.AddSingleton<ITaskOrchestratorService, TaskOrchestratorService>();
        builder.Services.AddSingleton<SelectionViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<DownloadPageViewModel>();

        builder.Services.AddSingleton<IWimProcessingService, WimProcessingService>();
        builder.Services.AddSingleton<IIsoCreationService, OscdimgIsoCreationService>();
        builder.Services.AddSingleton<IEsdToIsoConversionService, EsdToIsoConversionService>();

        // IHostedService registration order determines startup order:
        // 1. CacheService.StartAsync → EnsureSchemaAsync (must run before tasks are loaded)
        // 2. TaskOrchestratorService.StartAsync → LoadTasks
        builder.Services.AddHostedService(static sp =>
            (CacheService)sp.GetRequiredService<ICacheService>());
        builder.Services.AddHostedService(static sp =>
            (TaskOrchestratorService)sp.GetRequiredService<ITaskOrchestratorService>());

        return builder.Build();
    }
    // Guards against re-entrant close during async shutdown.
    private bool _isShuttingDown;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // StartAsync runs IHostedService.StartAsync for all registered hosted services
        // in registration order: CacheService (schema) → TaskOrchestratorService (load tasks).
        await _host!.StartAsync();

        _window = new MainWindow();
        MainWindow = _window;

        // Use AppWindow.Closing (fires before close) so we can perform async cleanup
        // and defer the actual window destruction until the host has stopped.
        _window.AppWindow.Closing += OnMainWindowClosing;

        _window.Activate();
    }

    private async void OnMainWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isShuttingDown)
            return; // Second close attempt — let it through.

        // Cancel the close; we will re-close once cleanup is done.
        args.Cancel = true;
        _isShuttingDown = true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _host!.StopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutdown timed out — proceed to dispose anyway.
        }
        catch (Exception)
        {
            // Unexpected error — proceed to dispose.
        }
        finally
        {
            _host!.Dispose();
            _window!.AppWindow.Closing -= OnMainWindowClosing;
            _window.Close(); // Now close for real.
        }
    }


}
