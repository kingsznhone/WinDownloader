using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WindowsImageDownloader.Views.Controls;

/// <summary>
/// A panel that arranges children left-to-right and wraps to the next row
/// when there is no more horizontal space, respecting each child's desired size.
/// </summary>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0d, OnSpacingChanged));

    /// <summary>Horizontal gap between items.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Vertical gap between rows.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnSpacingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, rowHeight = 0, totalHeight = 0;
        double hGap = HorizontalSpacing;
        double vGap = VerticalSpacing;
        bool firstInRow = true;

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(availableSize.Width, availableSize.Height));
            var desired = child.DesiredSize;

            double itemX = firstInRow ? 0 : x + hGap;

            if (!firstInRow && itemX + desired.Width > availableSize.Width)
            {
                // wrap to next row
                totalHeight += rowHeight + vGap;
                itemX = 0;
                rowHeight = 0;
                firstInRow = true;
            }

            x = itemX + desired.Width;
            rowHeight = Math.Max(rowHeight, desired.Height);
            firstInRow = false;
        }

        totalHeight += rowHeight;
        return new Size(availableSize.Width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        double hGap = HorizontalSpacing;
        double vGap = VerticalSpacing;
        bool firstInRow = true;

        var positions = new List<(UIElement child, double x, double y, Size size)>();

        foreach (UIElement child in Children)
        {
            var desired = child.DesiredSize;
            double itemX = firstInRow ? 0 : x + hGap;

            if (!firstInRow && itemX + desired.Width > finalSize.Width)
            {
                y += rowHeight + vGap;
                itemX = 0;
                rowHeight = 0;
                firstInRow = true;
            }

            positions.Add((child, itemX, y, desired));
            x = itemX + desired.Width;
            rowHeight = Math.Max(rowHeight, desired.Height);
            firstInRow = false;
        }

        foreach (var (child, cx, cy, size) in positions)
        {
            child.Arrange(new Rect(cx, cy, size.Width, size.Height));
        }

        return finalSize;
    }
}
