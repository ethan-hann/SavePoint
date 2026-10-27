using Avalonia.Input;

namespace Savepoint.App.Features.Common.Search;

/// <summary>The key that moves to the search box of the screen in front: Ctrl+F, and Cmd+F on macOS.</summary>
public static class FindKey
{
    /// <summary>Whether a key press asks to search, on this machine.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    public static bool Matches(Key key, KeyModifiers modifiers) => Matches(key, modifiers, OperatingSystem.IsMacOS());

    /// <summary>Whether a key press asks to search, on the named platform.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    /// <param name="isMacOS">True for macOS, where Cmd rather than Ctrl is the shortcut key.</param>
    public static bool Matches(Key key, KeyModifiers modifiers, bool isMacOS) =>
        key == Key.F && modifiers == (isMacOS ? KeyModifiers.Meta : KeyModifiers.Control);
}
