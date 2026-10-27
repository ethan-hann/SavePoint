using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Editor;

/// <summary>One removable, editable value in a <see cref="ChipListViewModel"/>.</summary>
public sealed partial class ChipItem : ObservableObject
{
    private readonly ChipListViewModel _list;
    private readonly UiFormatter _formatter;
    private string _text;

    internal ChipItem(ChipListViewModel list, UiFormatter formatter, string text)
    {
        _list = list;
        _formatter = formatter;
        _text = text;
        EditHint = formatter.Message("gameEditor.chip.editHint");
    }

    /// <summary>True while the chip is a text field.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>What has been typed into the chip's field.</summary>
    [ObservableProperty]
    private string _editText = string.Empty;

    /// <summary>The value.</summary>
    public string Text
    {
        get => _text;
        private set
        {
            if (SetProperty(ref _text, value))
            {
                OnPropertyChanged(nameof(RemoveLabel));
                OnPropertyChanged(nameof(EditLabel));
                OnPropertyChanged(nameof(EditFieldLabel));
            }
        }
    }

    /// <summary>The remove button's accessible name, already localized.</summary>
    public string RemoveLabel => Label("gameEditor.chip.remove");

    /// <summary>The accessible name of the chip itself, which opens it for editing.</summary>
    public string EditLabel => Label("gameEditor.chip.edit");

    /// <summary>The accessible name of the field the chip becomes while it is edited.</summary>
    public string EditFieldLabel => Label("gameEditor.chip.editField");

    /// <summary>The accessible help text of the chip, which says how to edit it.</summary>
    public string EditHint { get; }

    internal void SetText(string text) => Text = text;

    private string Label(string key) => _formatter.Message(key, ("value", _text), ("list", _list.GroupName));

    // Takes the value out of the list.
    [RelayCommand]
    private void Remove() => _list.Remove(this);

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Text;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitEdit()
    {
        // Leaving the field fires this after Enter or Escape already closed it.
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        _list.Replace(this, EditText);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        EditText = Text;
    }
}

/// <summary>
/// A list of short values shown as removable monospace chips with a dashed "add" chip that turns into a text field
/// (the ignore rules, launch options and mod folders of the game editor). Enter or leaving the field adds the value,
/// Escape cancels; a blank or repeated value is dropped without a message. A chip turns into the same kind of field to
/// change its value in place, with the same rules.
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
    /// <param name="groupName">The list's accessible name, such as "Files to leave out".</param>
    /// <param name="addLabel">The text of the add chip.</param>
    /// <param name="placeholder">The example shown in the empty add field.</param>
    /// <param name="addAutomationName">The accessible name of the add chip and its field when the visible text alone does not tell which list it adds to; defaults to <paramref name="addLabel"/>.</param>
    /// <param name="helpText">The accessible help text of the list, its add chip and its field; null for none.</param>
    public ChipListViewModel(UiFormatter text, string groupName, string addLabel, string placeholder, string? addAutomationName = null, string? helpText = null)
    {
        _text = text;
        GroupName = groupName;
        AddLabel = addLabel;
        Placeholder = placeholder;
        AddAutomationName = addAutomationName ?? addLabel;
        HelpText = helpText;
        Items.CollectionChanged += (_, _) => RebuildEntries();
        RebuildEntries();
    }

    /// <summary>Raised after the values changed through the chips (not through <see cref="SetValues"/>).</summary>
    public event EventHandler? Changed;

    /// <summary>The list's accessible name.</summary>
    public string GroupName { get; }

    /// <summary>The text of the add chip.</summary>
    public string AddLabel { get; }

    /// <summary>The accessible name of the add chip and its field.</summary>
    public string AddAutomationName { get; }

    /// <summary>The accessible help text of the list, its add chip and its field; null for none.</summary>
    public string? HelpText { get; }

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

        Items.Add(new ChipItem(this, _text, trimmed));
        return true;
    }

    internal void Remove(ChipItem chip)
    {
        if (!Items.Remove(chip))
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // A blank or repeated value, or one that leaves the chip as it was, keeps the chip as it is, as an add would drop it.
    internal void Replace(ChipItem chip, string value)
    {
        var trimmed = value.Trim();
        if (!Items.Contains(chip) || trimmed.Length == 0 || trimmed == chip.Text || Items.Any(other => other.Text == trimmed))
        {
            return;
        }

        chip.SetText(trimmed);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
