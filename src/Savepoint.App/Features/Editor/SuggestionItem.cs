using System.Windows.Input;

namespace Savepoint.App.Features.Editor;

/// <summary>One suggested value shown as a small button under a field.</summary>
/// <param name="Text">The value put into the field.</param>
/// <param name="Use">Puts the value into the field.</param>
/// <param name="Label">What the button says when that differs from the value, as a friendly name for a path shortcut; null to show the value.</param>
/// <param name="AccessibleName">The button's accessible name when it must say which field it fills; null to use the button's text.</param>
public sealed record SuggestionItem(string Text, ICommand Use, string? Label = null, string? AccessibleName = null)
{
    /// <summary>The button's text.</summary>
    public string Display => Label ?? Text;

    /// <summary>What a screen reader says for the button.</summary>
    public string AutomationName => AccessibleName ?? Display;
}
