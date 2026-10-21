namespace Savepoint.Core.Api;

/// <summary>Whether the user confirmed a folder for SavePoint, for the editor's "Confirmed for SavePoint on ..." line.</summary>
/// <param name="Confirmed">True when the folder carries a consent marker for this game.</param>
/// <param name="ConfirmedAt">When the user confirmed it, as an absolute instant; null when <paramref name="Confirmed"/> is false.</param>
public sealed record ConsentStatus(bool Confirmed, DateTimeOffset? ConfirmedAt);
