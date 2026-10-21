namespace Savepoint.Core.Api;

// Whether the user confirmed this folder for SavePoint, and when (the editor's "Confirmed for SavePoint on ..." line).
public sealed record ConsentStatus(bool Confirmed, DateTimeOffset? ConfirmedAt);
