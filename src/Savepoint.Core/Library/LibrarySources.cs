using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Library;

// Where a scan looks for installed games and which templates it matches them against.
internal sealed record LibrarySources(ISteamFolderLocator Steam, IGogInstalls? Gog, IReadOnlyList<GameTemplate> Templates);
