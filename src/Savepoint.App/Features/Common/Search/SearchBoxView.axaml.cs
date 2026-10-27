using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Savepoint.App.Features.Common.Search;

/// <summary>A search box; its DataContext is a <see cref="SearchBoxViewModel"/>.</summary>
public partial class SearchBoxView : UserControl
{
    /// <summary>Creates the view.</summary>
    public SearchBoxView()
    {
        InitializeComponent();
        Field.KeyDown += OnFieldKeyDown;
        ClearButton.Click += OnClearClicked;
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
}
