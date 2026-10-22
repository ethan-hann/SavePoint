using Avalonia.Input;

namespace Savepoint.App.Features.Help;

/// <summary>The keys that open the "How SavePoint works" page from anywhere: F1, and Cmd+? on macOS.</summary>
public static class HelpKey
{
    /// <summary>Whether a key press asks for help.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifier keys held with it.</param>
    public static bool Matches(Key key, KeyModifiers modifiers) =>
        (key == Key.F1 && modifiers == KeyModifiers.None)
        || (key == Key.OemQuestion && modifiers == (KeyModifiers.Meta | KeyModifiers.Shift));
}
