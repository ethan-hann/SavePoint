using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Catalog;

internal static class CatalogSamples
{
    public static GameDefinition ExeGame(string id = "skyrim-se-ab12") => new()
    {
        Id = id,
        Name = "Skyrim",
        Install = new InstallLocation(@"C:\Games\SkyrimSE"),
        External = new ExternalLocation(@"{documents}\My Games\Skyrim Special Edition\Saves"),
        Launch = new LaunchConfig(LaunchMethod.Exe, ExePath: "SkyrimSELauncher.exe", ProcessNameForExitDetection: "SkyrimSE.exe"),
        IgnorePaths = ["**/*.log"],
        ModPathHints = ["Data"],
    };

    public static GameDefinition UriGame(string id = "indie-cd34") => new()
    {
        Id = id,
        Name = "Indie",
        Install = new InstallLocation("/games/indie"),
        Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/489830"),
    };

    public static Profile Profile(string id = "modded-ab12", string gameId = "skyrim-se-ab12") => new()
    {
        Id = id,
        GameId = gameId,
        Name = "Modded",
        BranchName = $"profile/{id}",
    };

    public static AppSettings Settings(string storageRoot = "/data/savepoint") => AppSettings.Defaults(storageRoot);

    public static ValidationIssue Issue(string path, string messageId) => new(path, messageId);
}
