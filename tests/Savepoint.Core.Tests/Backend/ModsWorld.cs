using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// A game with the third folder: a mods folder outside the install, holding a few mod files.
internal static class ModsWorld
{
    public const string FirstMod = "modA/info.xml";

    // Like NewGame, with a mods folder next to the install and the saves folder; the folder exists and holds two files.
    public static GameDefinition NewGameWithMods(this BackendWorkspace workspace, string name = "Alpha", bool withSaves = true)
    {
        var game = workspace.NewGame(name, withSaves);
        var mods = Path.Combine(workspace.GamesRoot, game.Id, "mods");
        BackendWorkspace.WriteFile(Path.Combine(mods, "modA", "info.xml"), "mod a");
        BackendWorkspace.WriteFile(Path.Combine(mods, "modB", "info.xml"), "mod b");
        return game with { Mods = new ModsLocation(mods) };
    }

    public static string StoreOf(this BackendWorkspace workspace, GameDefinition game, string store) =>
        Path.Combine(workspace.DefaultStorageRoot, game.Id, store);
}
