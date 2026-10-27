using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The two-profile world is built once and copied, so a test must get what a hand-built rig holds, its own folders and program name, and
// nothing another test did to its copy.
public class TwoProfileTemplatesTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 150_000)]
    public async Task A_copy_holds_the_folders_a_rig_built_by_hand_holds()
    {
        var (copy, _, copyModded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = copy;
        using var byHand = await LaunchRig.CreateAsync();
        var (_, handModded) = await byHand.BuildTwoProfilesAsync();

        Assert.Equal(Named(byHand.SnapshotOf(RepoRole.Install), byHand.ExeName), Named(copy.SnapshotOf(RepoRole.Install), copy.ExeName));
        Assert.Equal(Named(byHand.SnapshotOf(RepoRole.External), byHand.ExeName), Named(copy.SnapshotOf(RepoRole.External), copy.ExeName));
        Assert.Equal(handModded.Name, copyModded.Name);
        Assert.Equal((await byHand.CommitsAsync(RepoRole.Install, handModded)).Count, (await copy.CommitsAsync(RepoRole.Install, copyModded)).Count);
        var install = await copy.StatusAsync(RepoRole.Install);
        Assert.Equal(copyModded.BranchName, install.Branch);
        Assert.False((await copy.PreviewAsync(Tip(copyModded))).Install.Dirty);
    }

    [Fact(Timeout = 150_000)]
    public async Task What_one_test_does_to_its_copy_is_not_there_for_the_next()
    {
        var (first, _, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _first = first;
        var before = first.SnapshotOf(RepoRole.Install);
        var savesBefore = first.SnapshotOf(RepoRole.External);
        first.WriteInstall("my-notes.txt", "written by the first test");
        first.WriteInstall("mods/a.txt", "changed by the first test");
        first.WriteSave("slot9.sav", "a save the first test made");
        first.WriteSave("slot1.sav", "changed by the first test");
        first.Dispose();

        var (second, _, secondModded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = second;

        Assert.False(second.InstallHas("my-notes.txt"));
        Assert.False(second.SavesHave("slot9.sav"));
        Assert.Equal(Named(before, first.ExeName), Named(second.SnapshotOf(RepoRole.Install), second.ExeName));
        Assert.Equal(Named(savesBefore, first.ExeName), Named(second.SnapshotOf(RepoRole.External), second.ExeName));
        Assert.False((await second.PreviewAsync(Tip(secondModded))).Install.Dirty);
        Assert.Equal(secondModded.BranchName, (await second.StatusAsync(RepoRole.Install)).Branch);
    }

    [Fact(Timeout = 150_000)]
    public async Task Tests_that_run_at_once_get_their_own_folders_and_program_names()
    {
        var (first, _, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _first = first;
        var (second, _, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _second = second;

        Assert.NotEqual(first.ExeName, second.ExeName);
        Assert.NotEqual(first.Workspace.Root, second.Workspace.Root);
        first.WriteInstall("only-in-first.txt", "first");
        Assert.False(second.InstallHas("only-in-first.txt"));
    }

    [Fact(Timeout = 150_000)]
    public async Task A_copy_takes_the_game_arguments_and_the_process_name_the_test_asks_for()
    {
        var (rig, _, _) = await LaunchRig.WithTwoProfilesAsync(gameArguments: ["--exit-after", "800"], followProcessName: true);
        using var _ = rig;

        var launch = rig.Game.Launch!;
        Assert.Equal(["--saves", rig.SavesPath, "--ready-file", rig.ReadyFile, "--exit-after", "800"], launch.Args);
        Assert.Equal(rig.ExeName, launch.ProcessNameForExitDetection);
    }

    // The program's file name differs between rigs, so it is replaced before two folders are compared; so does the time in the consent marker,
    // which names the game and folder but records when each rig confirmed it.
    private static Dictionary<string, string> Named(IReadOnlyDictionary<string, string> snapshot, string exeName) =>
        snapshot
            .Where(entry => entry.Key != ConsentMarker.FileName)
            .ToDictionary(entry => entry.Key.Replace(exeName, "program", StringComparison.Ordinal), entry => entry.Value);
}
