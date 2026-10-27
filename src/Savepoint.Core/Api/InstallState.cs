namespace Savepoint.Core.Api;

/// <summary>What the install folder holds: a game version and how many mod files sit on top of it.</summary>
/// <param name="Version">
/// The profile's version override when it has one, otherwise the version read from the game's program; null when neither exists.
/// </param>
/// <param name="ModFileCount">
/// The number of mod files: the install files the game's hints name plus every file of the game's mods folder at the same point. Null
/// when the game names no mod folders and has no mods folder snapshot, or the count is not known.
/// </param>
public sealed record InstallState(string? Version, int? ModFileCount);
