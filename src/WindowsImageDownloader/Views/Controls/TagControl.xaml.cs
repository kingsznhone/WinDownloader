using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsImageDownloader.Models;

namespace WindowsImageDownloader.Views.Controls;

/// <summary>
/// A pill-shaped label control inspired by Element Plus el-tag.
/// Supports semantic color variants via the <see cref="Type"/> property.
/// </summary>
public sealed partial class TagControl : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TagControl),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TypeProperty = DependencyProperty.Register(
        nameof(Type),
        typeof(TagType),
        typeof(TagControl),
        new PropertyMetadata(TagType.Default, OnTypeChanged));

    public TagControl()
    {
        InitializeComponent();
    }

    /// <summary>Gets or sets the label text displayed inside the tag.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets or sets the semantic color variant of the tag.</summary>
    public TagType Type
    {
        get => (TagType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    private static void OnTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TagControl)d).ApplyTypeState((TagType)e.NewValue);
    }

    private void ApplyTypeState(TagType type)
    {
        var stateName = type switch
        {
            TagType.Primary => "PrimaryState",
            TagType.Success  => "SuccessState",
            TagType.Warning  => "WarningState",
            TagType.Danger   => "DangerState",
            TagType.Info     => "InfoState",
            _                => "DefaultState",
        };

        VisualStateManager.GoToState(this, stateName, useTransitions: false);
    }
}
