using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Help;

/// <summary>
/// The six "What's this?" explanations, each built where its idea is on screen. Two sit on screens and end with a link to the
/// "How SavePoint works" page; the four that sit in dialogs have no link, because a page opened behind a dialog could not be used.
/// </summary>
public static class ContextualHelp
{
    /// <summary>"What's a snapshot?", beside the History title.</summary>
    /// <param name="text">Formats the copy.</param>
    /// <param name="openHowItWorks">Opens the page.</param>
    public static ContextualHelpViewModel Snapshot(UiFormatter text, Action openHowItWorks) =>
        new(text.Message("help.snapshot.question"), text.Message("help.snapshot.answer"), text.Message("help.moreInPage"), openHowItWorks);

    /// <summary>"What are new changes?", in the launch check's block of changed files.</summary>
    /// <param name="text">Formats the copy.</param>
    public static ContextualHelpViewModel NewChanges(UiFormatter text) =>
        new(text.Message("help.newChanges.question"), text.Message("help.newChanges.answer"));

    /// <summary>"What's a save snapshot?", under "Saves to load" in the launch check.</summary>
    /// <param name="text">Formats the copy.</param>
    public static ContextualHelpViewModel SaveSnapshot(UiFormatter text) =>
        new(text.Message("help.saveSnapshot.question"), text.Message("help.saveSnapshot.answer"));

    /// <summary>"Which should I pick?", under the older-snapshot note in the dialog after playing.</summary>
    /// <param name="text">Formats the copy.</param>
    /// <param name="profileName">The profile the session was played from.</param>
    public static ContextualHelpViewModel OlderChoice(UiFormatter text, string profileName) =>
        new(text.Message("help.olderChoice.question"), text.Message("help.olderChoice.answer", ("profileName", profileName)));

    /// <summary>"When should I replace?", on the Replace card in the dialog after playing.</summary>
    /// <param name="text">Formats the copy.</param>
    /// <param name="snapshotName">The snapshot Replace would overwrite.</param>
    public static ContextualHelpViewModel Replace(UiFormatter text, string snapshotName) =>
        new(text.Message("help.replace.question"), text.Message("help.replace.answer", ("snapshotName", snapshotName)));

    /// <summary>"How much space do snapshots take?", under the snapshot folder in Settings.</summary>
    /// <param name="text">Formats the copy.</param>
    /// <param name="openHowItWorks">Opens the page.</param>
    public static ContextualHelpViewModel Space(UiFormatter text, Action openHowItWorks) =>
        new(text.Message("help.space.question"), text.Message("help.space.answer"), text.Message("help.moreInPage"), openHowItWorks);
}
