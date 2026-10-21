namespace Savepoint.Core.Api;

/// <summary>What the install folder holds: a game version and how many mod files sit on top of it.</summary>
/// <param name="Version">
/// The profile's version override when it has one, otherwise the version read from the game's program; null when neither exists.
/// </param>
/// <param name="ModFileCount">The number of mod files, or null when the game names no mod folders or the count is not known.</param>
public sealed record InstallState(string? Version, int? ModFileCount);
