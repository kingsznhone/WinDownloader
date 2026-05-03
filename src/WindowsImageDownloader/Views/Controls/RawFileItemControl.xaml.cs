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

    public RawFileItemControl()
    {
        InitializeComponent();
    }

    public RawFileGroup? FileGroup
    {
        get => (RawFileGroup?)GetValue(FileGroupProperty);
        set => SetValue(FileGroupProperty, value);
    }
}
