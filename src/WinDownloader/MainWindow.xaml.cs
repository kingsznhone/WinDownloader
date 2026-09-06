using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinDownloader.ViewModels;
using WinDownloader.Views.Pages;
using WinRT.Interop;

namespace WinDownloader;

public sealed partial class MainWindow : Window
{
    private const uint WM_SETICON = 0x0080;
    private const nint ICON_SMALL = 0;
    private const nint ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

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
        SetTaskbarIcon();
        InitializeComponent();
    }

    // AppWindow.SetIcon only updates the small/title-bar icon on unpackaged apps.
    // The taskbar reads the icon set via WM_SETICON, so it must be set explicitly here.
    private void SetTaskbarIcon()
    {
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (!System.IO.File.Exists(iconPath))
            return;

        var hIcon = LoadImage(0, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (hIcon == 0)
            return;

        var hwnd = WindowNative.GetWindowHandle(this);
        SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
        SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
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
            "DownloadPage" => typeof(DownloadPage),
            "SettingsPage" => typeof(SettingsPage),
            _ => null
        };
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
            ContentFrame.Navigate(pageType);
    }

    private void NavigateToSelectionPage()
        => Navigate("SelectionPage");
}
