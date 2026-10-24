using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// A list of games whose items can be opened, not only selected: assistive technology that activates an item
/// opens its game, the same as a click does. It looks and styles exactly like a <see cref="ListBox"/>.
/// </summary>
public sealed class GameListBox : ListBox
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <summary>Raised when a game is opened through the item's automation peer.</summary>
    public event EventHandler<GameRowViewModel>? GameInvoked;

    /// <inheritdoc />
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<GameListBoxItem>(item, out recycleKey);

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new GameListBoxItem();

    internal void RaiseGameInvoked(GameRowViewModel row) => GameInvoked?.Invoke(this, row);
}

/// <summary>The item of a <see cref="GameListBox"/>; its automation peer can be invoked.</summary>
public sealed class GameListBoxItem : ListBoxItem
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBoxItem);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new GameItemAutomationPeer(this);

    private sealed class GameItemAutomationPeer(GameListBoxItem owner) : ListItemAutomationPeer(owner), IInvokeProvider
    {
        public void Invoke()
        {
            if (owner.DataContext is GameRowViewModel row && owner.FindAncestorOfType<GameListBox>() is { } list)
            {
                list.RaiseGameInvoked(row);
            }
        }
    }
}
