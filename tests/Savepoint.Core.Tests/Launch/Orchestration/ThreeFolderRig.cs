using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A launch rig whose game also has a mods folder outside the install, added through Edit game as a player does.
internal static class ThreeFolderRig
{
    public static string ModsPath(this LaunchRig rig) => rig.Game.Mods!.Path;

    public static void WriteMods(this LaunchRig rig, string relativePath, string content) => BackendWorkspace.WriteFile(Path.Combine(rig.ModsPath(), relativePath), content);

    public static string ReadMods(this LaunchRig rig, string relativePath) => File.ReadAllText(Path.Combine(rig.ModsPath(), relativePath));

    public static bool ModsHave(this LaunchRig rig, string relativePath) => File.Exists(Path.Combine(rig.ModsPath(), relativePath));

    public static IReadOnlyDictionary<string, string> ModsSnapshot(this LaunchRig rig) => BackendWorkspace.Snapshot(rig.ModsPath());

    // Gives the game a mods folder holding one mod; returns the folder.
    public static async Task<string> AddModsFolderAsync(this LaunchRig rig, string file = "modA/info.xml", string content = "mod a")
    {
        var mods = Path.Combine(rig.Workspace.GamesRoot, "later-mods");
        BackendWorkspace.WriteFile(Path.Combine(mods, file), content);
        await rig.EditGameAsync(game => game with { Mods = new ModsLocation(mods) });
        return mods;
    }

    // Vanilla is the game as installed with one mod in the mods folder; Modded adds two install mod files, patches a game file, adds a
    // second mod and a second save. The three folders end on Modded.
    public static async Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> WithTwoProfilesAndModsAsync(
        Func<BackendOptions, BackendOptions>? configure = null,
        IReadOnlyList<string>? gameArguments = null)
    {
        var rig = await LaunchRig.CreateAsync(configure, gameArguments);
        try
        {
            await rig.AddModsFolderAsync();
            var vanilla = await rig.ProfileAsync("Vanilla");
            rig.WriteInstall("mods/a.txt", "mod a");
            rig.WriteInstall("mods/b.txt", "mod b");
            rig.WriteInstall("data/base.pak", "a longer, patched base content");
            rig.WriteMods("modB/info.xml", "mod b");
            rig.WriteMods("modA/info.xml", "mod a, updated");
            rig.WriteSave("slot2.sav", "second save");
            return (rig, vanilla, await rig.ProfileAsync("Modded"));
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }
}
