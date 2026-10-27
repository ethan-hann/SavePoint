using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Savepoint.App.Features.Common.Search;

/// <summary>A search box; its DataContext is a <see cref="SearchBoxViewModel"/>.</summary>
public partial class SearchBoxView : UserControl
{
    private SearchBoxViewModel? _box;

    /// <summary>Creates the view.</summary>
    public SearchBoxView()
    {
        InitializeComponent();
        Field.KeyDown += OnFieldKeyDown;
        ClearButton.Click += OnClearClicked;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Puts the cursor in the box with its text selected, so typing replaces the last search. Does nothing, and says so, when the
    /// box is not showing.
    /// </summary>
    /// <returns>True when the box took focus.</returns>
    public bool FocusSearch()
    {
        if (!IsEffectivelyVisible || !IsEffectivelyEnabled || !Field.Focus(NavigationMethod.Tab))
        {
            return false;
        }

        Field.SelectAll();
        return true;
    }

    // Escape empties a box that has text; on an empty box it passes on, so it still closes whatever it would have closed.
    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && DataContext is SearchBoxViewModel { HasText: true } box)
        {
            box.ClearCommand.Execute(null);
            e.Handled = true;
        }
    }

    // The button goes away with the text, so focus goes back to the box where typing continues.
    private void OnClearClicked(object? sender, RoutedEventArgs e) => Field.Focus(NavigationMethod.Pointer);

    // A "Clear search" button elsewhere on the screen runs the same command; it too leaves with the text. A box that is not showing
    // (the games search has one in the rail and one on the shelf) cannot take focus, so only the one in view does.
    // Listens only while the view is on screen, so a screen that is let go is not kept alive by the box it showed.
    private void OnDataContextChanged(object? sender, EventArgs e) => Listen(TopLevel.GetTopLevel(this) is not null);

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Listen(true);
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Listen(false);
    }

    private void Listen(bool on)
    {
        if (_box is not null)
        {
            _box.Cleared -= OnBoxCleared;
            _box = null;
        }

        if (on && DataContext is SearchBoxViewModel box)
        {
            _box = box;
            box.Cleared += OnBoxCleared;
        }
    }

    private void OnBoxCleared(object? sender, EventArgs e) => Field.Focus(NavigationMethod.Tab);
}
