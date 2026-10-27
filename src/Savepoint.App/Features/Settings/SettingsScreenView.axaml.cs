using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Theme;

namespace Savepoint.App.Features.Settings;

/// <summary>The Settings screen's view; its DataContext is a <see cref="SettingsScreenViewModel"/>.</summary>
public partial class SettingsScreenView : UserControl
{
    private ThemeService? _theme;

    /// <summary>Creates the view.</summary>
    public SettingsScreenView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _theme = (Application.Current as App)?.Theme;
        if (_theme is not null)
        {
            _theme.UiScaleChanged += OnUiScaleChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_theme is not null)
        {
            _theme.UiScaleChanged -= OnUiScaleChanged;
            _theme = null;
        }
    }

    // A larger size shrinks the page's viewport and keeps its scroll offset, which can leave the choice that was just used below the fold.
    // Only a choice that was on screen is followed, so a size change made elsewhere never moves the page.
    private void OnUiScaleChanged(object? sender, EventArgs e)
    {
        var selector = ScaleSelector;
        if (selector.TransformToVisual(PageScroll) is not { } toPage)
        {
            return;
        }

        var box = new Rect(selector.Bounds.Size).TransformToAABB(toPage);
        if (box.Bottom < 0 || box.Top > PageScroll.Viewport.Height)
        {
            return;
        }

        // After the layout pass that applies the new size.
        Dispatcher.UIThread.Post(selector.BringIntoView, DispatcherPriority.Background);
    }
}
