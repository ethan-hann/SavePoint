namespace Savepoint.Core.Api;

// SuggestedName is read back from the stored name and is null when nothing readable is left; the UI supplies a fallback.
public sealed record RecoverableProfile(string BranchName, string? SuggestedName);
