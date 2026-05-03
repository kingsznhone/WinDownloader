using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.ViewModels;

namespace WindowsImageDownloader.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }
}
