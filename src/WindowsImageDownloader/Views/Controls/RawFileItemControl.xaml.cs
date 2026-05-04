using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Views.Controls;

public sealed partial class RawFileItemControl : UserControl
{
    public static readonly DependencyProperty FileGroupProperty = DependencyProperty.Register(
        nameof(FileGroup),
        typeof(RawFileGroup),
        typeof(RawFileItemControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DownloadCommandProperty = DependencyProperty.Register(
        nameof(DownloadCommand),
        typeof(ICommand),
        typeof(RawFileItemControl),
        new PropertyMetadata(null));

    public RawFileItemControl()
    {
        InitializeComponent();
    }

    public RawFileGroup? FileGroup
    {
        get => (RawFileGroup?)GetValue(FileGroupProperty);
        set => SetValue(FileGroupProperty, value);
    }

    /// <summary>
    /// Command invoked when the user clicks "下载 ESD".
    /// The command parameter is the <see cref="RawFile"/> from <see cref="FileGroup"/>.
    /// </summary>
    public ICommand? DownloadCommand
    {
        get => (ICommand?)GetValue(DownloadCommandProperty);
        set => SetValue(DownloadCommandProperty, value);
    }
}
