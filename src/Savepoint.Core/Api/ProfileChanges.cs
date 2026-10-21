namespace Savepoint.Core.Api;

/// <summary>
/// The fields of a profile a user edits. It carries only those, so an edit can never overwrite the launch time or branch name the
/// service owns.
/// </summary>
/// <param name="Name">The new name. Required.</param>
/// <param name="Notes">The new notes, or null for none.</param>
/// <param name="VersionOverride">The version to show instead of the detected one, or null to go back to the detected version (D13.6).</param>
public sealed record ProfileChanges(string Name, string? Notes, string? VersionOverride);
