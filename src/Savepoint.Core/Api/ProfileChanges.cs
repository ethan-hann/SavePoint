namespace Savepoint.Core.Api;

// Only the fields a user edits, so an edit can never overwrite the launch time or branch name the service owns.
public sealed record ProfileChanges(string Name, string? Notes, string? VersionOverride);
