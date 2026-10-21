using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

// History a removed game left at these folders, which the user can reconnect or set aside.
public sealed record OrphanedHistory(string ExistingGameId, IReadOnlyList<RepoRole> Roles, IReadOnlyList<RecoverableProfile> Profiles);
