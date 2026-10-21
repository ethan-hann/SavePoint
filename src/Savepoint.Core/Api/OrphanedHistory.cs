using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>History a removed game left at the folders a new game points at, which the user can reconnect or set aside.</summary>
/// <param name="ExistingGameId">The id of the removed game that left the history.</param>
/// <param name="Roles">Which of the new game's folders hold that history.</param>
/// <param name="Profiles">The profiles found in the old history, each with a suggested name.</param>
public sealed record OrphanedHistory(string ExistingGameId, IReadOnlyList<RepoRole> Roles, IReadOnlyList<RecoverableProfile> Profiles);
