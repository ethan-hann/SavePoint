using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Library;

/// <summary>
/// The path from "Add games from your libraries" to games in the library: the checklist, one confirmation that names every folder,
/// and the run that adds the games. Each step can be backed out of, and nothing is added before the confirmation.
/// </summary>
public sealed class LibraryImportFlow
{
    private readonly IGamesApi _games;
    private readonly IDialogs _dialogs;
    private readonly UiFormatter _text;
    private readonly LibraryRunTracker? _tracker;

    /// <param name="games">The games Api that scans and adds.</param>
    /// <param name="dialogs">Shows the three dialogs.</param>
    /// <param name="text">Formats the copy.</param>
    /// <param name="tracker">Tells the exit guard while games are being added; none when null.</param>
    public LibraryImportFlow(IGamesApi games, IDialogs dialogs, UiFormatter text, LibraryRunTracker? tracker = null)
    {
        _games = games;
        _dialogs = dialogs;
        _text = text;
        _tracker = tracker;
    }

    /// <summary>True when the last run ended because the player asked for the template list instead of choosing games.</summary>
    public bool TemplateListRequested { get; private set; }

    /// <summary>Runs the flow to the end.</summary>
    /// <returns>How the run ended, or null when the player backed out before anything was added.</returns>
    public async Task<LibraryAddSummary?> RunAsync()
    {
        var picker = new LibraryPickerDialogViewModel(_games, _text);
        var loading = picker.LoadAsync();
        var chosen = await _dialogs.ShowAsync(picker).ConfigureAwait(true);
        TemplateListRequested = picker.TemplateListRequested;
        if (chosen is not { Count: > 0 })
        {
            // A scan the player gave up on may still be blocked on a slow drive; it is not waited for.
            return null;
        }

        await loading.ConfigureAwait(true);

        if (!await _dialogs.ShowAsync(new LibraryConfirmDialogViewModel(chosen, _text)).ConfigureAwait(true))
        {
            return null;
        }

        var run = new LibraryAddDialogViewModel(_games, chosen, _text, _tracker);
        var running = run.RunAsync();
        var summary = await _dialogs.ShowAsync(run).ConfigureAwait(true);
        await running.ConfigureAwait(true);
        return summary;
    }
}
