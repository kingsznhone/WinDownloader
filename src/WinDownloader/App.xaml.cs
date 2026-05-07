using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Windows.Globalization;
using Microsoft.UI.Xaml;
using WinDownloader.Interfaces;
using WinDownloader.Iso;
using WinDownloader.Services;
using WinDownloader.ViewModels;
using WinDownloader.Wim;
using WinDownloader.Iso.Interfaces;

namespace WinDownloader;

public partial class App : Application
{
    private Window? _window;
    private IHost? _host;

    public App()
    {
        var settings = new AppSettingsService();
        settings.EnsureDefaults();
        ApplyLanguageOverride(settings);

        InitializeComponent();
        _host = BuildHost(settings);
    }

    public static IServiceProvider Services => ((App)Current)._host!.Services;
    public static Window MainWindow { get; private set; } = default!;
    public static T GetService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
    private static IHost BuildHost(AppSettingsService settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IAppSettings>(settings);
        builder.Services.AddSingleton<IUpdateCatalogService, UpdateCatalogService>();
        builder.Services.AddSingleton<ICacheService, CacheService>();
        builder.Services.AddSingleton<IDownloadService, DownloadService>();
        builder.Services.AddSingleton<IDownloadTaskPathService, DownloadTaskPathService>();
        builder.Services.AddSingleton<IEsdDownloadPipeline, EsdDownloadPipeline>();
        builder.Services.AddSingleton<SelectionViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<DownloadPageViewModel>();

        builder.Services.AddSingleton<IWimProcessingService, WimProcessingService>();
        builder.Services.AddSingleton<IIsoCreationService, OscdimgIsoCreationService>();
        builder.Services.AddSingleton<IEsdToIsoConversionService, EsdToIsoConversionService>();
        builder.Services.AddSingleton<EsdToIsoOrchestratorService>();
        builder.Services.AddSingleton<IEsdToIsoOrchestratorService>(static sp =>
            sp.GetRequiredService<EsdToIsoOrchestratorService>());
        builder.Services.AddSingleton<DownloadTaskOrchestratorService>();
        builder.Services.AddSingleton<IDownloadTaskOrchestratorService>(static sp =>
            sp.GetRequiredService<DownloadTaskOrchestratorService>());

        // IHostedService registration order determines startup order:
        // 1. CacheService.StartAsync → EnsureSchemaAsync (must run before tasks are loaded)
        // 2. DownloadTaskOrchestratorService.StartAsync → LoadTasks
        // 3. EsdToIsoOrchestratorService.StartAsync → no-op; StopAsync cancels conversion workers first
        builder.Services.AddHostedService(static sp =>
            (CacheService)sp.GetRequiredService<ICacheService>());
        builder.Services.AddHostedService(static sp =>
            sp.GetRequiredService<DownloadTaskOrchestratorService>());
        builder.Services.AddHostedService(static sp =>
            sp.GetRequiredService<EsdToIsoOrchestratorService>());

        return builder.Build();
    }
    // Guards against re-entrant close during async shutdown.
    private bool _isShuttingDown;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // StartAsync runs IHostedService.StartAsync for all registered hosted services
        // in registration order: CacheService (schema) → download orchestrator (load tasks) → ISO orchestrator.
        await _host!.StartAsync();

        _window = new MainWindow();
        MainWindow = _window;

        // Use AppWindow.Closing (fires before close) so we can perform async cleanup
        // and defer the actual window destruction until the host has stopped.
        _window.AppWindow.Closing += OnMainWindowClosing;

        _window.Activate();
    }

    private static void ApplyLanguageOverride(IAppSettings settings)
    {
        var language = settings.ResolveEffectiveLanguage();
        if (string.IsNullOrWhiteSpace(language))
            return;

        ApplicationLanguages.PrimaryLanguageOverride = language;

        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
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
