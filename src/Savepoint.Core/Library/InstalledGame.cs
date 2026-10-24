using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Library;

// One game a store says is installed, before any template is looked at.
internal sealed record InstalledGame(
    LibrarySource Source,
    string StoreId,
    string Name,
    string InstallPath,
    string FolderName,
    LaunchConfig Launch,
    int? SteamAppId);
