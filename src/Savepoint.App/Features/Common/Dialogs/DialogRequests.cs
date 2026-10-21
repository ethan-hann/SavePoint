namespace Savepoint.App.Features.Common.Dialogs;

/// <summary>A confirmation dialog.</summary>
/// <param name="Title">The headline, for example "Delete Skyrim?".</param>
/// <param name="Message">What will happen and whether it can be undone.</param>
/// <param name="ConfirmLabel">The confirm button, naming the action ("Delete game").</param>
/// <param name="Destructive">Draws the confirm button in the error color and does not make it the Enter default.</param>
/// <param name="CancelLabel">The cancel button; "Cancel" from the catalog when null.</param>
/// <param name="Typed">When set, the confirm button stays disabled until the user types the word.</param>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    bool Destructive = false,
    string? CancelLabel = null,
    TypedConfirmation? Typed = null);

/// <summary>A word the user has to type before a very destructive action goes ahead (clearing all data).</summary>
/// <param name="Word">The text that must be typed; compared ignoring case and surrounding spaces.</param>
/// <param name="Prompt">The line above the field, for example "Type DELETE to continue."</param>
public sealed record TypedConfirmation(string Word, string Prompt);

/// <summary>A folder picker request.</summary>
/// <param name="Title">The picker's title.</param>
/// <param name="StartPath">Where the picker opens, when that folder exists.</param>
public sealed record FolderPickRequest(string Title, string? StartPath = null);

/// <summary>A kind of file a picker offers, such as game definitions.</summary>
/// <param name="Name">What the user sees in the type list, already localized.</param>
/// <param name="Patterns">Glob patterns such as <c>*.json</c>.</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Patterns);

/// <summary>An open-file picker request.</summary>
/// <param name="Title">The picker's title.</param>
/// <param name="Filters">The file types offered; any file when null or empty.</param>
/// <param name="StartPath">Where the picker opens, when that folder exists.</param>
public sealed record OpenFilePickRequest(string Title, IReadOnlyList<FileTypeFilter>? Filters = null, string? StartPath = null);

/// <summary>A save-file picker request.</summary>
/// <param name="Title">The picker's title.</param>
/// <param name="SuggestedName">The file name offered, with its extension.</param>
/// <param name="Filters">The file types offered; the first sets the default extension.</param>
/// <param name="StartPath">Where the picker opens, when that folder exists.</param>
public sealed record SaveFilePickRequest(string Title, string SuggestedName, IReadOnlyList<FileTypeFilter>? Filters = null, string? StartPath = null);
