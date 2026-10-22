using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Features.Activity;

/// <summary>The Activity screen's view; its DataContext is an <see cref="ActivityScreenViewModel"/>.</summary>
public partial class ActivityScreenView : UserControl
{
    /// <summary>The width below which the jobs and the details stack instead of sitting side by side.</summary>
    public const double StackBelowWidth = 760;

    private ActivityScreenViewModel? _screen;
    private bool? _stacked;

    /// <summary>Creates the view.</summary>
    public ActivityScreenView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_screen is not null)
        {
            _screen.Announced -= OnAnnounced;
        }

        _screen = DataContext as ActivityScreenViewModel;
        if (_screen is not null)
        {
            _screen.Announced += OnAnnounced;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_screen is not null)
        {
            _screen.Announced -= OnAnnounced;
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_screen is not null)
        {
            _screen.Announced -= OnAnnounced;
            _screen.Announced += OnAnnounced;
        }
    }

    private void OnAnnounced(object? sender, string message) => Announcer.Announce(message);

    // Wide: the jobs and the details side by side. Narrow: the jobs above the details, each with the whole width.
    private void Arrange(double width)
    {
        var stacked = width < StackBelowWidth;
        if (_stacked == stacked)
        {
            return;
        }

        _stacked = stacked;
        Panes.ColumnDefinitions = ColumnDefinitions.Parse(stacked ? "*" : "5*,Auto,6*");
        Panes.RowDefinitions = RowDefinitions.Parse(stacked ? "*,Auto,*" : "*");
        Grid.SetRow(ListPane, 0);
        Grid.SetColumn(ListPane, 0);
        Grid.SetRow(PaneDivider, stacked ? 1 : 0);
        Grid.SetColumn(PaneDivider, stacked ? 0 : 1);
        Grid.SetRow(DetailsPane, stacked ? 2 : 0);
        Grid.SetColumn(DetailsPane, stacked ? 0 : 2);
        PaneDivider.Width = stacked ? double.NaN : 1;
        PaneDivider.Height = stacked ? 1 : double.NaN;
    }
}
