using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Views.Pages;
using Microsoft.UI.Xaml;

namespace WindowsImageDownloader;

public sealed partial class MainWindow : Microsoft.UI.Xaml.Window
{
    public MainWindow()
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1280;
            presenter.PreferredMinimumHeight = 720;
        }
        this.ExtendsContentIntoTitleBar = true;
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
            "SettingsPage"  => typeof(SettingsPage),
            _ => null
        };
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    private void NavigateToSelectionPage()
        => Navigate("SelectionPage");
}
