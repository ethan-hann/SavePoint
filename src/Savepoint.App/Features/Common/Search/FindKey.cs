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

    /// <summary>Whether a key press asks for the games search, on this machine.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    public static bool MatchesGames(Key key, KeyModifiers modifiers) => MatchesGames(key, modifiers, OperatingSystem.IsMacOS());

    /// <summary>
    /// Whether a key press asks for the games search, on the named platform: Shift added to the find key, so it can never be taken
    /// for the find key that goes to the search of the screen in front.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    /// <param name="isMacOS">True for macOS, where Cmd rather than Ctrl is the shortcut key.</param>
    public static bool MatchesGames(Key key, KeyModifiers modifiers, bool isMacOS) =>
        key == Key.F && modifiers == ((isMacOS ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift);
}
