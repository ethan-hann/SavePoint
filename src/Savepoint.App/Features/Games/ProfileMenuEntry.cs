using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Games;

/// <summary>
/// One entry of a profile row's menu. The row owns the list, and both menus that show it (More actions and the
/// right-click menu) bind to that one list, so they cannot drift apart.
/// </summary>
public sealed partial class ProfileMenuEntry : ObservableObject
{
    internal ProfileMenuEntry(string key, string header, ICommand? command = null, object? commandParameter = null, string? tip = null)
    {
        Key = key;
        Header = header;
        Command = command;
        CommandParameter = commandParameter;
        Tip = tip;
    }

    /// <summary>A stable name for the entry ("switch", "edit", "open:install"), for code that has to find one.</summary>
    public string Key { get; }

    /// <summary>The words on the entry; also its accessible name.</summary>
    public string Header { get; }

    /// <summary>What choosing the entry does; null for an entry that only explains why something is not offered.</summary>
    public ICommand? Command { get; }

    /// <summary>The value handed to <see cref="Command"/>.</summary>
    public object? CommandParameter { get; }

    /// <summary>A longer explanation shown as a tip, or null.</summary>
    public string? Tip { get; }

    /// <summary>False while the entry is not offered on this row.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>False while the entry is shown but cannot be chosen.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;
}
