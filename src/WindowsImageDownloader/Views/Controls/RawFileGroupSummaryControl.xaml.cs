using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Views.Controls;

public sealed partial class RawFileGroupSummaryControl : UserControl
{
    public static readonly DependencyProperty FileGroupProperty = DependencyProperty.Register(
        nameof(FileGroup),
        typeof(RawFileGroup),
        typeof(RawFileGroupSummaryControl),
        new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleTextProperty = DependencyProperty.Register(
        nameof(SubtitleText),
        typeof(string),
        typeof(RawFileGroupSummaryControl),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HashTextProperty = DependencyProperty.Register(
        nameof(HashText),
        typeof(string),
        typeof(RawFileGroupSummaryControl),
        new PropertyMetadata(string.Empty, OnHashTextChanged));

    public static readonly DependencyProperty HashTextVisibilityProperty = DependencyProperty.Register(
        nameof(HashTextVisibility),
        typeof(Visibility),
        typeof(RawFileGroupSummaryControl),
        new PropertyMetadata(Visibility.Collapsed));

    public RawFileGroupSummaryControl()
    {
        InitializeComponent();
    }

    public RawFileGroup? FileGroup
    {
        get => (RawFileGroup?)GetValue(FileGroupProperty);
        set => SetValue(FileGroupProperty, value);
    }

    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public string HashText
    {
        get => (string)GetValue(HashTextProperty);
        set => SetValue(HashTextProperty, value);
    }

    public Visibility HashTextVisibility
    {
        get => (Visibility)GetValue(HashTextVisibilityProperty);
        private set => SetValue(HashTextVisibilityProperty, value);
    }

    private static void OnHashTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (RawFileGroupSummaryControl)d;
        var hashText = e.NewValue as string;

        control.HashTextVisibility = string.IsNullOrWhiteSpace(hashText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
