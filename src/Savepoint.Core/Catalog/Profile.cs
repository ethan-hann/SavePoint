namespace Savepoint.Core.Catalog;

public sealed record Profile(
    string Id = "",
    string GameId = "",
    string Name = "",
    string BranchName = "",
    string? Notes = null,
    DateTimeOffset? LastLaunchedAt = null,
    string? VersionOverride = null);
