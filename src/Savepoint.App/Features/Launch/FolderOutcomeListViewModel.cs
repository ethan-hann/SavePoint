namespace Savepoint.App.Features.Launch;

/// <summary>One line of the partial-result card: a folder and whether it was saved.</summary>
/// <param name="Icon">A Material Symbols glyph: a check for saved, a cross for not.</param>
/// <param name="Text">"Game version and mods · saved".</param>
/// <param name="IsOk">True when the folder was saved.</param>
public sealed record FolderOutcomeRow(string Icon, string Text, bool IsOk);

/// <summary>The two-line status list inside the "Half of that save worked" card (mockup 2l).</summary>
/// <param name="Rows">One row per folder the capture touched.</param>
public sealed record FolderOutcomeListViewModel(IReadOnlyList<FolderOutcomeRow> Rows);
