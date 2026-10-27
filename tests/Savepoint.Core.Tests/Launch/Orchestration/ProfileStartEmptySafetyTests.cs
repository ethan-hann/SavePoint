using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A folder may start empty only when what it holds now is already in a snapshot of a profile of the game. Otherwise the launch that
// switches to the new profile would offer to discard the only copy of the player's files.
public class ProfileStartEmptySafetyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertRefusedAsUnsaved(OperationResult<JobStarted> refused)
    {
        Assert.False(refused.Ok);
        Assert.Equal("error.validation.invalidPayload", refused.Error!.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(refused.Error.Args["issues"]));
        Assert.Equal(ValidationMessageIds.ProfileStartEmptyUnsaved, issue.MessageId);
        Assert.Equal("startEmpty", issue.Path);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_first_profile_of_a_game_cannot_start_the_saves_folder_empty_because_no_snapshot_holds_those_saves()
    {
        using var rig = await LaunchRig.CreateAsync();
        var saves = rig.SnapshotOf(RepoRole.External);

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

        AssertRefusedAsUnsaved(refused);
        Assert.Empty((await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!);
        Assert.Equal(saves, rig.SnapshotOf(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_with_a_new_file_since_its_profile_cannot_start_empty()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        rig.WriteSave("slot9.sav", "played after the snapshot");

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

        AssertRefusedAsUnsaved(refused);
        Assert.Equal(["Vanilla"], (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Select(profile => profile.Name));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_with_a_changed_file_since_its_profile_cannot_start_empty()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        rig.WriteSave("slot1.sav", "overwritten after the snapshot");

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

        AssertRefusedAsUnsaved(refused);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_mods_folder_added_after_the_profile_was_made_cannot_start_empty_and_nothing_is_created()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        await rig.AddModsFolderAsync();
        var mods = rig.ModsSnapshot();

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External, RepoRole.Mods]);

        AssertRefusedAsUnsaved(refused);
        Assert.Equal(["Vanilla"], (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Select(profile => profile.Name));
        Assert.Equal(mods, rig.ModsSnapshot());
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_says_which_folders_are_held_in_a_snapshot_and_which_are_not()
    {
        using var rig = await LaunchRig.CreateAsync();

        var first = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;
        await rig.ProfileAsync("Vanilla");
        var clean = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;
        rig.WriteSave("slot9.sav", "new");
        var changed = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;

        Assert.All(first.Folders, folder => Assert.False(folder.HeldInSnapshot));
        Assert.True(clean.Folders.Single(folder => folder.Role == RepoRole.External).HeldInSnapshot);
        Assert.False(clean.Folders.Single(folder => folder.Role == RepoRole.Install).HeldInSnapshot);
        Assert.False(changed.Folders.Single(folder => folder.Role == RepoRole.External).HeldInSnapshot);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_names_why_a_folder_cannot_start_empty()
    {
        using var rig = await LaunchRig.CreateAsync();

        var first = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;
        await rig.ProfileAsync("Vanilla");
        var clean = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;
        rig.WriteSave("slot9.sav", "new");
        var changed = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;

        Assert.Equal(StartEmptyBlock.FirstProfile, first.Folders.Single(folder => folder.Role == RepoRole.External).StartEmptyBlocked);
        Assert.Equal(StartEmptyBlock.None, clean.Folders.Single(folder => folder.Role == RepoRole.External).StartEmptyBlocked);
        Assert.Equal(StartEmptyBlock.NewChanges, changed.Folders.Single(folder => folder.Role == RepoRole.External).StartEmptyBlocked);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_blames_a_stopped_switch_when_the_note_of_one_cannot_be_read()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), RepoRole.External);
        File.WriteAllText(SwitchJournal.PathIn(gitDir), "{ this is not a note");

        var preview = (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!;

        var saves = preview.Folders.Single(folder => folder.Role == RepoRole.External);
        Assert.False(saves.HeldInSnapshot);
        Assert.Equal(StartEmptyBlock.StoppedSwitch, saves.StartEmptyBlocked);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_retry_of_a_folder_that_failed_to_start_empty_is_refused_when_the_folder_gained_files_in_between()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.AddModsFolderAsync();
        await rig.ProfileAsync("Vanilla");
        var away = rig.ModsPath() + "-away";
        BackendWorkspace.MoveFolder(rig.ModsPath(), away);
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Test run", null, "Test run", [RepoRole.Mods]);
        Assert.True(started.Ok, started.Error?.Detail);
        var first = Assert.IsType<CaptureResult>((await rig.WaitForJobAsync(started.Value.JobId)).Result);
        Assert.False(first.Folders.Single(folder => folder.Role == RepoRole.Mods).Ok);
        BackendWorkspace.MoveFolder(away, rig.ModsPath());
        rig.WriteMods("modC/info.xml", "added by the player while the retry waited");
        var made = (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Name == "Test run");

        var retry = await rig.CaptureAsync(made, "Test run", RepoRole.Mods);

        var folder = Assert.IsType<CaptureResult>(retry.Result).Folders.Single();
        Assert.False(folder.Ok);
        Assert.Equal("error.validation.invalidPayload", folder.Error!.MessageId);
        Assert.True(rig.ModsHave("modC/info.xml"));
    }
}
