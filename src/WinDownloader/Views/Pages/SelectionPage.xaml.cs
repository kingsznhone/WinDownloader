using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDownloader.ViewModels;

namespace WinDownloader.Views.Pages;

public sealed partial class SelectionPage : Page
{
    public SelectionViewModel ViewModel { get; }

    public SelectionPage()
    {
        ViewModel = App.GetService<SelectionViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.EnsureCatalogLoadedAsync();
    }
}
