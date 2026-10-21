using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Editor;

/// <summary>One removable value in a <see cref="ChipListViewModel"/>.</summary>
/// <param name="Text">The value.</param>
/// <param name="RemoveLabel">The remove button's accessible name, already localized.</param>
/// <param name="Remove">Takes the value out of the list.</param>
public sealed record ChipItem(string Text, string RemoveLabel, ICommand Remove);

/// <summary>
/// A list of short values shown as removable monospace chips with a dashed "add" chip that turns into a text field
/// (the ignore rules, launch options and mod folders of the game editor). Enter or leaving the field adds the value,
/// Escape cancels; a blank or repeated value is dropped without a message.
/// </summary>
public sealed partial class ChipListViewModel : ObservableObject
{
    private readonly UiFormatter _text;

    /// <summary>True while the add chip is a text field.</summary>
    [ObservableProperty]
    private bool _isAdding;

    /// <summary>What has been typed into the add field.</summary>
    [ObservableProperty]
    private string _newText = string.Empty;

    /// <param name="text">Formats the remove buttons' names.</param>
    /// <param name="groupName">The list's accessible name, such as "Files to ignore".</param>
    /// <param name="addLabel">The text of the add chip.</param>
    /// <param name="placeholder">The example shown in the empty add field.</param>
    public ChipListViewModel(UiFormatter text, string groupName, string addLabel, string placeholder)
    {
        _text = text;
        GroupName = groupName;
        AddLabel = addLabel;
        Placeholder = placeholder;
        Items.CollectionChanged += (_, _) => RebuildEntries();
        RebuildEntries();
    }

    /// <summary>Raised after the values changed through the chips (not through <see cref="SetValues"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>The list's accessible name.</summary>
    public string GroupName { get; }

    /// <summary>The text of the add chip.</summary>
    public string AddLabel { get; }

    /// <summary>The example shown in the empty add field.</summary>
    public string Placeholder { get; }

    /// <summary>The chips, in order.</summary>
    public ObservableCollection<ChipItem> Items { get; } = [];

    /// <summary>The values, in order.</summary>
    public IReadOnlyList<string> Values => [.. Items.Select(chip => chip.Text)];

    /// <summary>What the view lays out in one wrapping row: every <see cref="ChipItem"/>, then this list itself as the add chip.</summary>
    public ObservableCollection<object> Entries { get; } = [];

    /// <summary>Replaces the whole list, dropping blank and repeated values; raises nothing.</summary>
    public void SetValues(IEnumerable<string> values)
    {
        Items.Clear();
        foreach (var value in values)
        {
            TryAdd(value);
        }
    }

    [RelayCommand]
    private void BeginAdd()
    {
        NewText = string.Empty;
        IsAdding = true;
    }

    [RelayCommand]
    private void CommitAdd()
    {
        // Leaving the field fires this after Enter or Escape already closed it.
        if (!IsAdding)
        {
            return;
        }

        var added = TryAdd(NewText);
        IsAdding = false;
        NewText = string.Empty;
        if (added)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void CancelAdd()
    {
        IsAdding = false;
        NewText = string.Empty;
    }

    private void RebuildEntries()
    {
        Entries.Clear();
        foreach (var chip in Items)
        {
            Entries.Add(chip);
        }

        Entries.Add(this);
    }

    private bool TryAdd(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || Items.Any(chip => chip.Text == trimmed))
        {
            return false;
        }

        Items.Add(new ChipItem(trimmed, _text.Message("gameEditor.chip.remove", ("value", trimmed)), new RelayCommand(() => Remove(trimmed))));
        return true;
    }

    private void Remove(string value)
    {
        var chip = Items.FirstOrDefault(item => item.Text == value);
        if (chip is null)
        {
            return;
        }

        Items.Remove(chip);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
