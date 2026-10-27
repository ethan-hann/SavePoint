using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Editor;

/// <summary>A chip list; its DataContext is a <see cref="ChipListViewModel"/>.</summary>
public partial class ChipListView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ChipListView()
    {
        InitializeComponent();
        AddHandler(Button.ClickEvent, OnClick, RoutingStrategies.Bubble);
        AddHandler(DoubleTappedEvent, OnDoubleTapped, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnEditKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Border>(includeSelf: true) is { Name: "ChipText", DataContext: ChipItem chip })
        {
            chip.BeginEditCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Enter or Escape closes the field, which drops keyboard focus out of the form; it goes back to the chip. Leaving the
    // field some other way keeps the focus where the player put it. The key binding handles Enter and Escape first, so this
    // also listens for handled keys.
    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || e.Source is not ChipEntryBox { DataContext: ChipItem chip } || DataContext is not ChipListViewModel list)
        {
            return;
        }

        var index = list.Items.IndexOf(chip);
        Dispatcher.UIThread.Post(() => FocusChipText(index), DispatcherPriority.Loaded);
    }

    private void FocusChipText(int index)
    {
        var chips = Chips.GetVisualDescendants().OfType<Border>().Where(border => border.Name == "ChipText").ToList();
        if (index >= 0 && index < chips.Count)
        {
            chips[index].Focus(NavigationMethod.Tab);
        }
    }

    // Removing a chip rebuilds every chip, which would drop keyboard focus out of the form; it goes to the chip that took the
    // removed one's place, or to the add chip after the last one.
    private void OnClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button { DataContext: ChipItem chip } || DataContext is not ChipListViewModel list)
        {
            return;
        }

        var index = list.Items.IndexOf(chip);
        Dispatcher.UIThread.Post(() => FocusAfterRemoval(index), DispatcherPriority.Loaded);
    }

    private void FocusAfterRemoval(int index)
    {
        var buttons = Chips.GetVisualDescendants().OfType<Button>().ToList();
        var removers = buttons.Where(button => button.DataContext is ChipItem).ToList();
        var next = removers.Count > 0
            ? removers[Math.Min(index, removers.Count - 1)]
            : buttons.FirstOrDefault(button => button.Name == "AddChip");
        next?.Focus(NavigationMethod.Tab);
    }
}
