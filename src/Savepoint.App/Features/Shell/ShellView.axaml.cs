using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Search;

namespace Savepoint.App.Features.Shell;

/// <summary>The shell's chrome: the rail or the top bar and shelf, around the one content region. Its DataContext is a <see cref="ShellViewModel"/>.</summary>
public partial class ShellView : UserControl
{
    /// <summary>Creates the view; the window pushes its width to the view model, which decides when the rail collapses.</summary>
    public ShellView()
    {
        InitializeComponent();

        // A tooltip opens on the pointer alone, so keyboard focus on a game in the collapsed rail opens it too: that is the only place the full name shows.
        var games = this.FindControl<ListBox>("RailGames")!;
        games.AddHandler(GotFocusEvent, OnGameFocused, RoutingStrategies.Bubble);
        games.AddHandler(LostFocusEvent, OnGameBlurred, RoutingStrategies.Bubble);

        // Selecting a game only marks it; a click or Enter on it opens it, even when it is already the selected one.
        // The list marks Enter handled before it bubbles, so the key handler also listens to handled events.
        foreach (var name in new[] { "RailGames", "ShelfGames" })
        {
            var list = this.FindControl<GameListBox>(name)!;
            list.GameInvoked += (_, row) => OpenGame(row);
            list.AddHandler(TappedEvent, OnGameTapped, RoutingStrategies.Bubble);
            list.AddHandler(KeyDownEvent, OnGameKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    /// <summary>
    /// Puts the cursor in the search box of the screen in front; a screen with none leaves it to the games' search box, in the rail or
    /// on the shelf, whichever shows.
    /// </summary>
    /// <returns>True when a search box took focus.</returns>
    public bool FocusSearch()
    {
        var screenBoxes = this.FindControl<ContentControl>("ContentRegion")!.GetVisualDescendants().OfType<SearchBoxView>();
        return screenBoxes.Concat([this.FindControl<SearchBoxView>("RailSearch")!, this.FindControl<SearchBoxView>("ShelfSearch")!]).Any(box => box.FocusSearch());
    }

    private void OnGameTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: GameRowViewModel row })
        {
            OpenGame(row);
        }
    }

    private void OnGameKeyDown(object? sender, KeyEventArgs e)
    {
        // The focused item, not the selected one: Ctrl+arrow moves focus without selecting.
        if (e.Key == Key.Enter && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: GameRowViewModel row })
        {
            OpenGame(row);
            e.Handled = true;
        }
    }

    private void OpenGame(GameRowViewModel row) => (DataContext as ShellViewModel)?.SelectGame(row.Id);

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // The shell's own width equals the window's, so the rail collapses with it even when the host does not report widths.
        if (DataContext is ShellViewModel shell)
        {
            shell.SetWindowWidth(e.NewSize.Width);
            shell.SetWindowHeight(e.NewSize.Height);
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is ShellViewModel shell && Bounds.Width > 0)
        {
            shell.SetWindowWidth(Bounds.Width);
            shell.SetWindowHeight(Bounds.Height);
        }
    }

    private static void OnGameFocused(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is ListBoxItem item && e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional && ToolTip.GetTip(item) is not null)
        {
            ToolTip.SetIsOpen(item, true);
        }
    }

    private static void OnGameBlurred(object? sender, RoutedEventArgs e)
    {
        if (e.Source is ListBoxItem item)
        {
            ToolTip.SetIsOpen(item, false);
        }
    }
}
