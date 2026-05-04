using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.ViewModels;

namespace WindowsImageDownloader.Views.Pages;

public sealed partial class DownloadPage : Page
{
    public DownloadPageViewModel ViewModel { get; }

    public DownloadPage()
    {
        ViewModel = App.GetService<DownloadPageViewModel>();
        InitializeComponent();
    }
}
