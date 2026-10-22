namespace Savepoint.App.Features.Launch;

/// <summary>One line of the partial-result card: a folder and whether it was saved.</summary>
/// <param name="Icon">A Material Symbols glyph: a check for saved, a cross for failed, an info mark for a folder the save left as it was.</param>
/// <param name="Text">"Game version and mods · saved".</param>
/// <param name="IsOk">True when the folder was saved.</param>
/// <param name="IsLeftAlone">True when the save left the folder as it was: not saved, and not a failure either.</param>
public sealed record FolderOutcomeRow(string Icon, string Text, bool IsOk, bool IsLeftAlone = false)
{
    /// <summary>True when the folder failed, which the row shows in the error color.</summary>
    public bool IsFailed => !IsOk && !IsLeftAlone;
}

/// <summary>The two-line status list inside the "Half of that save worked" card (mockup 2l).</summary>
/// <param name="Rows">One row per folder the capture touched.</param>
public sealed record FolderOutcomeListViewModel(IReadOnlyList<FolderOutcomeRow> Rows);
