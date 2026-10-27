namespace Savepoint.App.Features.Shell;

public sealed partial class ShellViewModel
{
    /// <summary>
    /// What a screen reader is told with the rail's Add a game button while the icon rail has no games: the "No games yet." note, which
    /// has no room to show there. Null otherwise, because the wide rail shows the note itself.
    /// </summary>
    public string? RailAddHelpText => IsCompact && ShowFirstRun ? _deps.Text.Message("shell.noGamesYet") : null;
}
