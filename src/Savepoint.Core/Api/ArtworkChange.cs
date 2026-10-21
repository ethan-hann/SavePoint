namespace Savepoint.Core.Api;

/// <summary>Raised when the artwork of one game may now look different: an image arrived, was chosen, was removed, or went stale.</summary>
/// <param name="GameId">The game whose artwork changed. Read it again with <see cref="IArtworkApi.GetAsync"/>.</param>
public sealed record ArtworkChange(string GameId);
