using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WindowsImageDownloader.Services;
using WindowsImageDownloader.ViewModels;

namespace WindowsImageDownloader;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    public static IServiceProvider Services { get; private set; } = default!;

    public static Window MainWindow { get; private set; } = default!;

    public static T GetService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        MainWindow = window;
        window.Closed += (_, _) => (Services as IDisposable)?.Dispose();
        window.Activate();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAppSettings, AppSettingsService>(static _ =>
        {
            var settings = new AppSettingsService();
            settings.EnsureDefaults();
            return settings;
        });
        services.AddSingleton<IUpdateCatalogService, UpdateCatalogService>();
        services.AddSingleton<IWimProcessingService, WimProcessingService>();
        services.AddTransient<SelectionViewModel>();
        services.AddTransient<SettingsViewModel>();
        return services.BuildServiceProvider();
    }
}
