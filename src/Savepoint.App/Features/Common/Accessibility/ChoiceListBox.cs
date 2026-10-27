using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace Savepoint.App.Features.Common.Accessibility;

/// <summary>
/// A choice of one among a few options, such as how to start a game or which theme to use. It looks and styles exactly like a
/// <see cref="ListBox"/>, and assistive technology meets it as a radio group instead of a list: a named group whose options are
/// radio buttons that each say whether they are selected. All four arrow keys move to the next or previous option, wrapping at the
/// ends, whatever the layout the options are arranged in.
/// </summary>
public sealed class ChoiceListBox : ListBox
{
    /// <summary>Whether an arrow key pressed on the last option goes to the first and the other way round. True by default.</summary>
    public static readonly StyledProperty<bool> WrapsAtEndsProperty = AvaloniaProperty.Register<ChoiceListBox, bool>(nameof(WrapsAtEnds), defaultValue: true);

    /// <summary>
    /// Whether an arrow key pressed on the last option goes to the first and the other way round. A size scale turns it off, so one
    /// key at the largest size can never drop the choice to the smallest.
    /// </summary>
    public bool WrapsAtEnds
    {
        get => GetValue(WrapsAtEndsProperty);
        set => SetValue(WrapsAtEndsProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <inheritdoc />
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<ChoiceListBoxItem>(item, out recycleKey);

    /// <inheritdoc />
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new ChoiceListBoxItem();

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ChoiceGroupPeer(this);

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        // The list's own handling skips options laid out in a grid; a key pressed inside an option keeps that usual handling.
        if (!e.Handled && e.KeyModifiers == KeyModifiers.None && e.Source is ChoiceListBoxItem option && ItemCount > 1 && StepFor(e.Key) is { } step)
        {
            var from = IndexFromContainer(option);
            if (from >= 0 && MoveFrom(from, step))
            {
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    // Down and Right go forward and Up and Left go back; in a right-to-left layout Left and Right trade places.
    private int? StepFor(Key key)
    {
        var sideways = FlowDirection == FlowDirection.RightToLeft ? -1 : 1;
        return key switch
        {
            Key.Down => 1,
            Key.Up => -1,
            Key.Right => sideways,
            Key.Left => -sideways,
            _ => null,
        };
    }

    // Selects the next option that can be chosen and puts focus on it. Without wrapping, a key past the last option that can be
    // chosen does nothing (and is still taken, so the list's own handling cannot wrap).
    private bool MoveFrom(int from, int step)
    {
        for (var tried = 1; tried < ItemCount; tried++)
        {
            var raw = from + (step * tried);
            if (!WrapsAtEnds && (raw < 0 || raw >= ItemCount))
            {
                return true;
            }

            var index = ((raw % ItemCount) + ItemCount) % ItemCount;
            ScrollIntoView(index);
            if (ContainerFromIndex(index) is { IsEnabled: true } container)
            {
                SelectedIndex = index;
                container.Focus(NavigationMethod.Directional);
                return true;
            }
        }

        return false;
    }

    private sealed class ChoiceGroupPeer(ChoiceListBox owner) : ListBoxAutomationPeer(owner)
    {
        // Windows and Linux have no radio-group role; macOS has one, but Avalonia maps no control type to it. A named group of radio buttons is what each gets.
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}

/// <summary>The option of a <see cref="ChoiceListBox"/>; assistive technology meets it as a radio button that knows whether it is selected.</summary>
public sealed class ChoiceListBoxItem : ListBoxItem
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ListBoxItem);

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new ChoiceOptionPeer(this);

    // Besides the selection pattern, the option is a toggle: the macOS and Linux bridges read a radio button's state from it only.
    private sealed class ChoiceOptionPeer : ListItemAutomationPeer, IToggleProvider
    {
        private readonly ChoiceListBoxItem _owner;

        public ChoiceOptionPeer(ChoiceListBoxItem owner)
            : base(owner)
        {
            _owner = owner;
            owner.PropertyChanged += (_, e) =>
            {
                if (e.Property == SelectingItemsControl.IsSelectedProperty)
                {
                    RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, StateOf(e.OldValue is true), StateOf(e.NewValue is true));
                }
            };
        }

        public ToggleState ToggleState => StateOf(_owner.IsSelected);

        // A radio button is chosen, never cleared: toggling the chosen one changes nothing.
        public void Toggle() => _owner.IsSelected = true;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.RadioButton;

        private static ToggleState StateOf(bool selected) => selected ? ToggleState.On : ToggleState.Off;
    }
}
