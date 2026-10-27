using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

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

    public static string FolderOf(GameDefinition game, RepoRole role) => role switch
    {
        RepoRole.Install => game.Install.Path,
        RepoRole.Mods => game.Mods!.Path,
        _ => game.External!.Path,
    };

    // The profile's commits in one folder's store, newest first.
    public static async Task<IReadOnlyList<CommitInfo>> CommitsAsync(this BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        return await storage.Engine.ListCommitsAsync(new RepoRef(storage.GitDirectory(game.Id, role), FolderOf(game, role), game.Id, role), profile.BranchName, null, workspace.Token);
    }

    // The files the profile's newest commit holds in one folder's store.
    public static async Task<IReadOnlyList<string>> FilesAsync(this BackendWorkspace workspace, GameDefinition game, RepoRole role, Profile profile)
    {
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        return await storage.Engine.ListTreeFilesAsync(new RepoRef(storage.GitDirectory(game.Id, role), FolderOf(game, role), game.Id, role), profile.BranchName, workspace.Token);
    }

    public static string StoreOf(this BackendWorkspace workspace, GameDefinition game, string store) =>
        Path.Combine(workspace.DefaultStorageRoot, game.Id, store);
}
