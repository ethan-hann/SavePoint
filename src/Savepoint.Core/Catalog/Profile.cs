namespace Savepoint.Core.Catalog;

/// <summary>
/// One named setup of a game (a version with its mods and saves). Each profile is a branch in the game's repositories, so its
/// history is kept apart from the others.
/// </summary>
/// <param name="Id">Stable id made by <see cref="CatalogIds.NewProfile"/>; it never changes after the profile is created.</param>
/// <param name="GameId">The id of the game the profile belongs to.</param>
/// <param name="Name">The name the user sees. Required.</param>
/// <param name="BranchName">
/// The git branch holding this profile's history, <c>profile/</c> plus the id. Owned by the service and never edited by the user;
/// letters, digits, <c>/</c>, <c>_</c>, and <c>-</c> only, always lowercase when SavePoint makes it.
/// </param>
/// <param name="Notes">Free text the user keeps about the profile, or null.</param>
/// <param name="LastLaunchedAt">When the profile was last launched, or null if it never was. Set by the service when a launch succeeds.</param>
/// <param name="VersionOverride">
/// A version the user typed in place of the one read from the game's program (D13.6), or null to use the detected version.
/// </param>
public sealed record Profile(
    string Id = "",
    string GameId = "",
    string Name = "",
    string BranchName = "",
    string? Notes = null,
    DateTimeOffset? LastLaunchedAt = null,
    string? VersionOverride = null);
