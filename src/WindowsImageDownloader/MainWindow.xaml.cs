using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Views.Pages;

namespace WindowsImageDownloader;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RootNavigation_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        RootNavigation.SelectedItem ??= SelectionNavigationItem;
        NavigateToSelectionPage();
    }

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: "SelectionPage" })
        {
            NavigateToSelectionPage();
        }
    }

    private void NavigateToSelectionPage()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(SelectionPage))
        {
            ContentFrame.Navigate(typeof(SelectionPage));
        }
    }
}
