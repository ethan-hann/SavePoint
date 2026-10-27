using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Savepoint.App.Features.Shell;

/// <summary>The two-segment layout toggle; its DataContext is a <see cref="LayoutToggleViewModel"/>.</summary>
public partial class LayoutToggleView : UserControl
{
    private LayoutToggleViewModel? _model;

    /// <summary>Creates the toggle; set its DataContext to <see cref="IShellContext.LayoutToggle"/>.</summary>
    public LayoutToggleView()
    {
        InitializeComponent();
        // The list changes its choice while it handles the key, so the view marks the key before it and unmarks it after.
        AddHandler(KeyDownEvent, OnKeyDownBeforeList, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDownAfterList, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow(DataContext as LayoutToggleViewModel);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Follow(null);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (TopLevel.GetTopLevel(this) is not null)
        {
            Follow(DataContext as LayoutToggleViewModel);
        }
    }

    private void Follow(LayoutToggleViewModel? model)
    {
        if (ReferenceEquals(model, _model))
        {
            return;
        }

        if (_model is not null)
        {
            _model.FocusRequested -= OnFocusRequested;
        }

        _model = model;
        if (_model is not null)
        {
            _model.FocusRequested += OnFocusRequested;
        }
    }

    private static bool IsArrow(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down;

    private void OnKeyDownBeforeList(object? sender, KeyEventArgs e)
    {
        if (IsArrow(e.Key) && e.KeyModifiers == KeyModifiers.None)
        {
            _model?.BeginKeyboardChoice();
        }
    }

    private void OnKeyDownAfterList(object? sender, KeyEventArgs e) => _model?.EndKeyboardChoice();

    // Both layouts hold a copy of the toggle; only the one on screen takes focus, on the option that is chosen.
    private void OnFocusRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!IsEffectivelyVisible || this.FindControl<ListBox>("Toggle") is not { SelectedIndex: >= 0 } list)
                {
                    return;
                }

                (list.ContainerFromIndex(list.SelectedIndex) ?? list).Focus(NavigationMethod.Directional);
            },
            DispatcherPriority.Loaded);
}
