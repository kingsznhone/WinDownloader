using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Views.Controls;

public sealed partial class RawFileItemControl : UserControl
{
    public static readonly DependencyProperty RawFileProperty = DependencyProperty.Register(
        nameof(RawFile),
        typeof(RawFile),
        typeof(RawFileItemControl),
        new PropertyMetadata(null));

    public RawFileItemControl()
    {
        InitializeComponent();
    }

    public RawFile? RawFile
    {
        get => (RawFile?)GetValue(RawFileProperty);
        set => SetValue(RawFileProperty, value);
    }
}
