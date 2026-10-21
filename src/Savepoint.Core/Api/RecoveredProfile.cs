namespace Savepoint.Core.Api;

// The name the user will see for a reconnected profile, chosen by the UI from the suggestion or its fallback copy.
public sealed record RecoveredProfile(string BranchName, string Name);
