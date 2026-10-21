namespace Savepoint.Core.Git;

/// <summary>Whether a folder carries the consent marker of the game and role asked about.</summary>
/// <param name="Confirmed">True only when the marker names exactly that game and role.</param>
/// <param name="ConfirmedAt">When the marker was written; null when <paramref name="Confirmed"/> is false.</param>
public sealed record RepoConsent(bool Confirmed, DateTimeOffset? ConfirmedAt);
