using System.Windows.Input;

namespace Savepoint.App.Features.Editor;

/// <summary>One suggested value shown as a small button under a field.</summary>
/// <param name="Text">The value, which is also the button's text.</param>
/// <param name="Use">Puts the value into the field.</param>
public sealed record SuggestionItem(string Text, ICommand Use);
