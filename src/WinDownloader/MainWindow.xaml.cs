// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    public DownloadPageViewModel DownloadViewModel { get; } = App.GetService<DownloadPageViewModel>();

    public MainWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 1080;
            presenter.PreferredMinimumHeight = 720;
        }
        float scale = GetDpiScale();
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(1080 * scale), (int)(720 * scale)));
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("favicon.ico");
        SetTaskbarIcon();
        InitializeComponent();
    }

    private float GetDpiScale()
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        return GetDpiForWindow(hwnd) / 96f;
    }

    // AppWindow.SetIcon only updates the small/title-bar icon on unpackaged apps.
    // The taskbar reads the icon set via WM_SETICON, so it must be set explicitly here.
    private void SetTaskbarIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        nint hIcon = LoadImage(0, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (hIcon == 0)
        {
            return;
        }

        nint hwnd = WindowNative.GetWindowHandle(this);
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
        Type? pageType = tag switch
        {
            "SelectionPage" => typeof(SelectionPage),
            "DownloadPage" => typeof(DownloadPage),
            "SettingsPage" => typeof(SettingsPage),
            _ => null
        };
        if (pageType is not null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void NavigateToSelectionPage()
        => Navigate("SelectionPage");
}
