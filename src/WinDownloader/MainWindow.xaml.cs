using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinDownloader.ViewModels;
using WinDownloader.Views.Pages;
using Microsoft.UI.Xaml;

namespace WinDownloader;

public sealed partial class MainWindow : Window
{
    public DownloadPageViewModel DownloadViewModel { get; } = App.GetService<DownloadPageViewModel>();

    public MainWindow()
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1280;
            presenter.PreferredMinimumHeight = 720;
        }
        this.ExtendsContentIntoTitleBar = true;
        this.AppWindow.SetIcon("favicon.ico");
        InitializeComponent();
    }

    private void RootNavigation_Loaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.SelectedItem ??= SelectionNavigationItem;
        NavigateToSelectionPage();
    }

    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag);
        }
    }

    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "SelectionPage" => typeof(SelectionPage),
            "DownloadPage"  => typeof(DownloadPage),
            "SettingsPage"  => typeof(SettingsPage),
            _ => null
        };
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    private void NavigateToSelectionPage()
        => Navigate("SelectionPage");
}
