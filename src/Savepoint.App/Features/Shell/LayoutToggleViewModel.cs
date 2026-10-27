using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>One side of the layout toggle.</summary>
/// <param name="Layout">The layout it selects.</param>
/// <param name="Label">Its name, already localized.</param>
/// <param name="Icon">A Material Symbols glyph.</param>
public sealed record LayoutOption(ShellLayout Layout, string Label, string Icon);

/// <summary>
/// The two-segment Sidebar / Library control. It sits at the right of the Games header in the sidebar layout and in
/// the top bar of the library layout; place it with <c>LayoutToggleView</c> and this view model from
/// <see cref="IShellContext.LayoutToggle"/>. Choosing a segment asks the shell to save and switch the layout.
/// </summary>
public sealed partial class LayoutToggleViewModel : ObservableObject
{
    private readonly Func<ShellLayout, Task> _setLayout;
    private bool _syncing;
    private bool _keyboardChoice;

    /// <summary>The selected segment; setting it asks the shell to switch layout.</summary>
    [ObservableProperty]
    private LayoutOption? _selected;

    /// <param name="text">Supplies the segment names and the control's accessible name.</param>
    /// <param name="setLayout">Called when the user picks a segment; must not throw.</param>
    /// <param name="initial">The layout shown selected at first.</param>
    public LayoutToggleViewModel(UiFormatter text, Func<ShellLayout, Task> setLayout, ShellLayout initial)
    {
        _setLayout = setLayout;
        Options =
        [
            new LayoutOption(ShellLayout.Sidebar, text.Message("shell.layout.sidebar"), Icons.LeftPanelOpen),
            new LayoutOption(ShellLayout.Library, text.Message("shell.layout.library"), Icons.GridView),
        ];
        AutomationName = text.Message("shell.layout.label");
        _selected = Options.First(option => option.Layout == initial);
    }

    /// <summary>The two segments, Sidebar first.</summary>
    public IReadOnlyList<LayoutOption> Options { get; }

    /// <summary>The control's accessible name ("Layout").</summary>
    public string AutomationName { get; }

    /// <summary>Selects the segment for a layout without asking the shell to change anything; the shell calls it when the layout changed by another route.</summary>
    public void Sync(ShellLayout layout)
    {
        _syncing = true;
        try
        {
            Selected = Options.First(option => option.Layout == layout);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Raised when a layout switch that began with an arrow key has swapped in the other layout's copy of the toggle, so the copy on
    /// screen can take focus. Raised again if the switch failed to save and the layout was put back.
    /// </summary>
    public event EventHandler? FocusRequested;

    /// <summary>Marks the key press in progress as an arrow key on the toggle; the view calls it before the list handles the key.</summary>
    public void BeginKeyboardChoice() => _keyboardChoice = true;

    /// <summary>Ends the mark made by <see cref="BeginKeyboardChoice"/>; the view calls it once the list has handled the key.</summary>
    public void EndKeyboardChoice() => _keyboardChoice = false;

    partial void OnSelectedChanged(LayoutOption? value)
    {
        // A list box clears its selection while it rebuilds; only a real choice counts.
        if (!_syncing && value is not null)
        {
            _ = SwitchAsync(value.Layout, _keyboardChoice);
        }
    }

    // The shell shows the other layout's copy of the toggle at once, and the copy that held focus is hidden with its layout, so a key
    // press asks for focus to follow the choice.
    private async Task SwitchAsync(ShellLayout layout, bool viaKeyboard)
    {
        var switching = _setLayout(layout);
        if (viaKeyboard)
        {
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }

        await switching;
        if (viaKeyboard)
        {
            FocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
