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
