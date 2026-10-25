using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// "Update existing" replaces the one point the player named and never rewrites another (D31).
public class LaunchUpdateExistingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // Heavy mods is made while Vanilla is loaded, with a mod file added first; the saves did not change, so its saves history is Vanilla's.
    private static async Task<(Profile Vanilla, Profile Heavy)> VanillaAndHeavyAsync(LaunchRig rig)
    {
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteInstall("mods/big.bin", "a very large mod");
        return (vanilla, await rig.ProfileAsync("Heavy mods"));
    }

    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, Profile profile)
    {
        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(profile));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", "played on");
        rig.KillGame();
        return Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_profile_whose_saves_are_another_profile_s_is_not_offered_the_other_profile_s_snapshot_to_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        var vanillaSave = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];
        Assert.Equal(vanillaSave.Id, (await rig.CommitsAsync(RepoRole.External, heavy))[0].Id);

        var exited = await PlayAsync(rig, heavy);

        Assert.Null(exited.Options.UpdateTarget);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
        Assert.Equal([CompletionChoice.CaptureNew, CompletionChoice.Skip], exited.Options.Choices);
    }

    // Heavy mods gets a snapshot of its own in both folders, so it has something that is its own to replace.
    private static async Task<(Profile Vanilla, Profile Heavy)> HeavyWithItsOwnSnapshotAsync(LaunchRig rig)
    {
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        await rig.StartAsync(Tip(heavy));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", "first session");
        rig.WriteInstall("mods/more.bin", "more");
        rig.KillGame();
        await rig.WaitForAsync<SessionState.Exited>();
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Heavy session"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();
        return (vanilla, heavy);
    }

    [Fact(Timeout = 200_000)]
    public async Task Replacing_the_played_profile_s_own_newest_snapshot_leaves_the_profile_it_was_made_from_as_it_was_and_ends_without_a_warning()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await HeavyWithItsOwnSnapshotAsync(rig);
        var vanillaSaves = (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Id).ToList();
        var vanillaInstall = (await rig.CommitsAsync(RepoRole.Install, vanilla)).Select(commit => commit.Id).ToList();
        var exited = await PlayAsync(rig, heavy);
        Assert.Equal("Heavy session", exited.Options.UpdateTarget?.Name);

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Heavy session"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.False(folder.KeptAsNew || folder.LeftAlone || !folder.Ok));
        Assert.Equal(vanillaSaves, (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Id));
        Assert.Equal(vanillaInstall, (await rig.CommitsAsync(RepoRole.Install, vanilla)).Select(commit => commit.Id));
        Assert.Equal(["Heavy session", "Vanilla"], (await rig.CommitsAsync(RepoRole.External, heavy)).Select(commit => commit.Subject));
        var played = await rig.WithEngineAsync((engine, repo) => engine.ReadBlobAsync(repo, heavy.BranchName, "slot1.sav", rig.Token), RepoRole.External);
        Assert.Equal("played on", System.Text.Encoding.UTF8.GetString(played!));
    }

    [Fact(Timeout = 200_000)]
    public async Task The_profile_another_was_made_from_is_not_offered_a_replace_of_a_snapshot_the_other_profile_still_holds()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, _) = await VanillaAndHeavyAsync(rig);

        var exited = await PlayAsync(rig, vanilla);

        Assert.Null(exited.Options.UpdateTarget);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_replace_is_not_offered_after_a_duplicate_holds_the_snapshot_and_the_duplicate_is_never_offered_the_original_s()
    {
        using var rig = await LaunchRig.CreateAsync();
        var original = await rig.ProfileAsync("Original");
        var copied = await rig.Profiles.DuplicateAsync(rig.Game.Id, original.Id, "Copy");
        Assert.True(copied.Ok, copied.Error?.Detail);

        // The game also writes to its own folder, so the replacement would rewrite the install point the copy continues from.
        await rig.StartAsync(Tip(original));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", "played on");
        rig.WriteInstall("settings.ini", "written by the game");
        rig.KillGame();
        var first = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, first.Options.Choices);
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Original session"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();

        var second = await PlayAsync(rig, copied.Value);

        Assert.Null(second.Options.UpdateTarget);
    }

    [Fact(Timeout = 200_000)]
    public async Task In_a_chain_of_profiles_made_from_one_another_no_profile_is_offered_a_snapshot_another_one_holds()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (_, b) = await HeavyWithItsOwnSnapshotAsync(rig);
        rig.WriteInstall("mods/c.bin", "c");
        var c = await rig.ProfileAsync("C");

        // C continues B's history, so B's newest snapshot is C's too and neither may rewrite it.
        var onC = await PlayAsync(rig, c);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, onC.Options.Choices);
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("C session"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();
        var onB = await PlayAsync(rig, b);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, onB.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task The_snapshot_offered_is_the_newest_the_profile_owns_in_either_folder_and_only_when_every_folder_written_ends_on_it()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(vanilla, "Second save", RepoRole.External);
        await Task.Delay(1100, Ct);
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);

        // The newest snapshot is the install-only one; it has no saves point, so a saves change would have to be a new snapshot.
        var exited = await PlayAsync(rig, vanilla);

        Assert.Null(exited.Options.UpdateTarget);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task The_newest_snapshot_is_offered_when_the_only_folder_written_ends_on_it()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(vanilla, "Second save", RepoRole.External);

        var exited = await PlayAsync(rig, vanilla);

        Assert.Equal("Second save", exited.Options.UpdateTarget?.Name);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_duplicate_with_no_point_of_its_own_offers_no_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        var copied = await rig.Profiles.DuplicateAsync(rig.Game.Id, vanilla.Id, "Copy");
        Assert.True(copied.Ok, copied.Error?.Detail);

        var exited = await PlayAsync(rig, copied.Value);

        Assert.Null(exited.Options.UpdateTarget);
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
        Assert.Contains(CompletionChoice.CaptureNew, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_profile_made_from_another_offers_the_saves_it_started_with()
    {
        using var rig = await LaunchRig.CreateAsync();
        var (vanilla, heavy) = await VanillaAndHeavyAsync(rig);
        var vanillaSave = (await rig.CommitsAsync(RepoRole.External, vanilla))[0];

        var preview = await rig.PreviewAsync(Tip(heavy));

        Assert.Contains(vanillaSave.CaptureId, preview.AvailableSaves.Select(save => save.Id.Value));
    }

    [Fact(Timeout = 200_000)]
    public async Task An_update_leaves_an_install_only_point_alone_when_the_install_changed_during_play()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        // The install's newest point (a mod update) belongs to another capture than the saves' newest point.
        await Task.Delay(1100, Ct);
        rig.WriteInstall("mods/update.txt", "a mod update");
        await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(vanilla, "Second save", RepoRole.External);
        var modUpdate = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];

        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.WriteInstall("settings.ini", "written by the game");
        rig.WriteSave("slot2.sav", "played on");
        rig.KillGame();
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        // The newest snapshot has no install commit of its own, and the install changed, so a replacement would have to split it in two.
        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
        var refused = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"));
        Assert.Equal("error.launch.completionNotOffered", refused.Error?.MessageId);
        Assert.Equal(["Mod update", "Vanilla"], (await rig.CommitsAsync(RepoRole.Install, vanilla)).Select(commit => commit.Subject));
        Assert.Equal(modUpdate.Id, (await rig.CommitsAsync(RepoRole.Install, vanilla))[0].Id);
        Assert.Equal(["Second save", "Vanilla"], (await rig.CommitsAsync(RepoRole.External, vanilla)).Select(commit => commit.Subject));
    }
}
