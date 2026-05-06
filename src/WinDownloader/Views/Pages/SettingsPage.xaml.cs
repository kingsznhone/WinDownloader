using Microsoft.UI.Xaml.Controls;
using WinDownloader.ViewModels;

namespace WinDownloader.Views.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }
}
