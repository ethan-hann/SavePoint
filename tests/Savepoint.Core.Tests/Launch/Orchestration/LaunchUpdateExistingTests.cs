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

    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, Profile profile, string save = "played on")
    {
        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(profile));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", save);
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
        Assert.Contains(CompletionChoice.UpdateExisting, exited.Options.Choices);

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok && !folder.KeptAsNew, folder.Error?.Detail));

        // The mod update point is still there, in the install's history and in the history list.
        var install = await rig.CommitsAsync(RepoRole.Install, vanilla);
        Assert.Contains(modUpdate.Id, install.Select(commit => commit.Id));
        Assert.Equal(["Updated", "Mod update", "Vanilla"], install.Select(commit => commit.Subject));
        var history = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!;
        var kept = Assert.Single(history.Points, point => point.Name == "Mod update");
        Assert.Equal(HistoryPointKind.VersionAndMods, kept.Kind);

        // The update stays one point: the saves point was replaced in place and the install change joined it.
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        Assert.Equal(["Updated", "Vanilla"], saves.Select(commit => commit.Subject));
        Assert.Equal(result.PointId?.Value, install[0].CaptureId);
        Assert.Equal(result.PointId?.Value, saves[0].CaptureId);
        Assert.Contains("settings.ini", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, vanilla.BranchName, rig.Token), RepoRole.Install));
    }

    private static async Task<SessionState.Exited> PlayWithoutChangesAsync(LaunchRig rig, Profile profile)
    {
        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(profile));
        await rig.WaitForGameAsync();
        rig.KillGame();
        return Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
    }

    private static async Task<SessionState.Completed> CompleteAsync(LaunchRig rig, SessionCompletion completion)
    {
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, completion)).Ok);
        return Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
    }

    [Fact(Timeout = 200_000)]
    public async Task Nothing_unsaved_at_exit_offers_no_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        var exited = await PlayWithoutChangesAsync(rig, vanilla);

        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_change_only_to_files_the_game_ignores_offers_no_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        Assert.True((await rig.Workspace.Games.UpdateAsync(rig.Game with { IgnorePaths = ["logs/"] })).Ok);
        var vanilla = await rig.ProfileAsync("Vanilla");

        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.WriteInstall("logs/today.log", "noise");
        rig.KillGame();
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_saves_folder_that_is_gone_at_exit_offers_no_replace()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.KillGame();
        Directory.Delete(rig.SavesPath, recursive: true);
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.DoesNotContain(CompletionChoice.UpdateExisting, exited.Options.Choices);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_folder_that_changes_after_exit_and_whose_snapshot_another_profile_holds_is_saved_as_a_new_snapshot_and_says_so()
    {
        using var rig = await LaunchRig.CreateAsync();
        var original = await rig.ProfileAsync("Original");
        var copied = await rig.Profiles.DuplicateAsync(rig.Game.Id, original.Id, "Copy");
        Assert.True(copied.Ok, copied.Error?.Detail);
        var installBefore = (await rig.CommitsAsync(RepoRole.Install, original)).Select(commit => commit.Id).ToList();
        var exited = await PlayAsync(rig, original);

        // Only the saves were unsaved at exit, and they are the original's own; the install point is the copy's too.
        Assert.Contains(CompletionChoice.UpdateExisting, exited.Options.Choices);
        rig.WriteInstall("settings.ini", "written after the game closed");
        var done = await CompleteAsync(rig, new SessionCompletion.UpdateExisting("Original"));

        Assert.Null(done.Error);
        var result = Assert.IsType<CaptureResult>(done.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok && folder.KeptAsNew, folder.Error?.Detail));
        var install = await rig.CommitsAsync(RepoRole.Install, original);
        Assert.Equal(installBefore, install.Skip(1).Select(commit => commit.Id));
        Assert.Equal(installBefore, (await rig.CommitsAsync(RepoRole.Install, copied.Value)).Select(commit => commit.Id));
        Assert.Equal(2, (await rig.CommitsAsync(RepoRole.External, original)).Count);
    }

    [Fact(Timeout = 200_000)]
    public async Task A_duplicate_s_original_replaces_its_own_snapshot_once_it_has_one_the_duplicate_does_not_hold()
    {
        using var rig = await LaunchRig.CreateAsync();
        var original = await rig.ProfileAsync("Original");
        var copied = await rig.Profiles.DuplicateAsync(rig.Game.Id, original.Id, "Copy");
        Assert.True(copied.Ok, copied.Error?.Detail);
        await PlayAsync(rig, original);
        rig.WriteInstall("mods/own.bin", "own");
        await CompleteAsync(rig, new SessionCompletion.CaptureNew("Original session"));

        var exited = await PlayAsync(rig, original, "played again");

        Assert.Equal("Original session", exited.Options.UpdateTarget?.Name);
        var done = await CompleteAsync(rig, new SessionCompletion.UpdateExisting("Original session"));
        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok && !folder.KeptAsNew && !folder.LeftAlone));
        Assert.Equal(["Original session", "Original"], (await rig.CommitsAsync(RepoRole.External, original)).Select(commit => commit.Subject));
    }

    [Fact(Timeout = 200_000)]
    public async Task A_single_profile_can_replace_a_saves_only_snapshot_and_then_one_that_also_wrote_the_install()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");

        await PlayAsync(rig, vanilla);
        var first = await CompleteAsync(rig, new SessionCompletion.UpdateExisting("Vanilla"));
        Assert.Null(first.Error);

        File.Delete(rig.ReadyFile);
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot1.sav", "second session");
        rig.WriteInstall("settings.ini", "written by the game");
        rig.KillGame();
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.Contains(CompletionChoice.UpdateExisting, exited.Options.Choices);

        var second = await CompleteAsync(rig, new SessionCompletion.UpdateExisting("Vanilla"));

        Assert.Null(second.Error);
        var result = Assert.IsType<CaptureResult>(second.Result);
        Assert.All(result.Folders, folder => Assert.True(folder.Ok && !folder.KeptAsNew));
        var install = await rig.CommitsAsync(RepoRole.Install, vanilla);
        var saves = await rig.CommitsAsync(RepoRole.External, vanilla);
        Assert.Equal(result.PointId?.Value, install[0].CaptureId);
        Assert.Equal(result.PointId?.Value, saves[0].CaptureId);
        Assert.Single(saves);
        Assert.Equal(2, install.Count);
    }
}
