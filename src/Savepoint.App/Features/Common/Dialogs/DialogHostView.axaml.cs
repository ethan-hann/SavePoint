using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>
/// Shows the dialog on top of a <see cref="DialogHostViewModel"/> over the window. Escape and a click on the scrim
/// dismiss it when the dialog allows that; focus moves into the dialog when it opens and goes back to whatever had
/// it before the first dialog opened.
/// </summary>
public partial class DialogHostView : UserControl
{
    private DialogHostViewModel? _host;
    private IInputElement? _returnFocus;
    private int _depth;

    /// <summary>Creates the overlay; give it a <see cref="DialogHostViewModel"/> as its DataContext.</summary>
    public DialogHostView()
    {
        InitializeComponent();
        Scrim.PointerPressed += OnScrimPressed;
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_host is not null)
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
        }

        _host = DataContext as DialogHostViewModel;
        _depth = _host?.Stack.Count ?? 0;
        if (_host is not null)
        {
            _host.PropertyChanged += OnHostPropertyChanged;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape && _host is { IsOpen: true })
        {
            _host.RequestDismissCurrent();
            e.Handled = true;
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        _host?.RequestScrimDismiss();
        e.Handled = true;
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogHostViewModel.Current) || _host is null)
        {
            return;
        }

        var depth = _host.Stack.Count;
        if (depth > _depth)
        {
            CloseOpenToolTips();
            if (_depth == 0)
            {
                _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            }

            Dispatcher.UIThread.Post(FocusFirstControl, DispatcherPriority.Loaded);
        }
        else if (depth < _depth)
        {
            Dispatcher.UIThread.Post(depth == 0 ? RestoreFocus : FocusFirstControl, DispatcherPriority.Loaded);
        }

        _depth = depth;
    }

    // A tooltip lives in the popup layer, so the scrim does not cover it; on Linux one opened by hover would stay on top of the dialog.
    private void CloseOpenToolTips()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        foreach (var control in top.GetVisualDescendants().OfType<Control>().Where(ToolTip.GetIsOpen).ToList())
        {
            ToolTip.SetIsOpen(control, false);
        }
    }

    private void FocusFirstControl()
    {
        if (_host is not { IsOpen: true })
        {
            return;
        }

        Chrome.UpdateLayout();
        var first = Chrome.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(element => element.Focusable && element.IsTabStop && element.IsEffectivelyEnabled && element.IsEffectivelyVisible);
        first?.Focus(NavigationMethod.Tab);
    }

    private void RestoreFocus()
    {
        var target = _returnFocus;
        _returnFocus = null;
        if (target is Visual visual && visual.IsAttachedToVisualTree())
        {
            target.Focus();
        }
    }
}
