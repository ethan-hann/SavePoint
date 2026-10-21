namespace Savepoint.Core.Api;

/// <summary>A profile found in old history that can be brought back.</summary>
/// <param name="BranchName">The profile's branch in the old history; it identifies the profile when reconnecting.</param>
/// <param name="SuggestedName">
/// A name read back from what was stored, or null when nothing readable is left. The UI supplies fallback copy for null.
/// </param>
public sealed record RecoverableProfile(string BranchName, string? SuggestedName);
