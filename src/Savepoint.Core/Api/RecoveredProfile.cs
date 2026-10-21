namespace Savepoint.Core.Api;

/// <summary>A profile the user chose to bring back, with the name it will have.</summary>
/// <param name="BranchName">The branch of the profile to restore, from the matching <see cref="RecoverableProfile"/>.</param>
/// <param name="Name">The name the user will see, chosen by the UI from the suggestion or its fallback copy.</param>
public sealed record RecoveredProfile(string BranchName, string Name);
