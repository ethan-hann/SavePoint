using Avalonia.Input;

namespace Savepoint.App.Features.Activity;

/// <summary>The key that copies the selected lines: Ctrl+C, and Cmd+C on macOS.</summary>
public static class CopyKey
{
    /// <summary>Whether a key press asks to copy, on this machine.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    public static bool Matches(Key key, KeyModifiers modifiers) => Matches(key, modifiers, OperatingSystem.IsMacOS());

    /// <summary>Whether a key press asks to copy, on the named platform.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    /// <param name="isMacOS">True for macOS, where Cmd rather than Ctrl is the shortcut key.</param>
    public static bool Matches(Key key, KeyModifiers modifiers, bool isMacOS) =>
        key == Key.C && modifiers == (isMacOS ? KeyModifiers.Meta : KeyModifiers.Control);
}
