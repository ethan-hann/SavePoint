using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Editor;

/// <summary>
/// An extra button drawn in a <see cref="ChipListViewModel"/>'s row after its add chip, such as the pickers that fill a list from the
/// file system. The owner of the list supplies the command and keeps the help text current.
/// </summary>
public sealed partial class ChipListAction : ObservableObject
{
    /// <param name="label">The button's text.</param>
    /// <param name="automationName">The button's accessible name, which says which list it fills.</param>
    /// <param name="command">What the button does; the button is disabled while the command cannot run.</param>
    public ChipListAction(string label, string automationName, ICommand command)
    {
        Label = label;
        AutomationName = automationName;
        Command = command;
    }

    /// <summary>The button's text.</summary>
    public string Label { get; }

    /// <summary>The button's accessible name.</summary>
    public string AutomationName { get; }

    /// <summary>What the button does.</summary>
    public ICommand Command { get; }

    /// <summary>The button's accessible help text, such as why it is disabled; null for none.</summary>
    [ObservableProperty]
    private string? _helpText;
}
