namespace Savepoint.App.Features.Games;

public sealed partial class ProfileRowViewModel
{
    /// <summary>
    /// What the row's state means, for the two states that have no room for a button: "New changes" and "Loaded now". It is the row's
    /// help text for assistive technology and its tooltip; null for a row in neither state.
    /// </summary>
    public string? HelpText => (IsUnsavedLine, IsActiveLine) switch
    {
        (true, _) => _text.Message("help.row.newChanges"),
        (_, true) => _text.Message("help.row.loaded"),
        _ => null,
    };
}
