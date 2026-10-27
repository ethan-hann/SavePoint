using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Backend;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The empty-start check on folders in unusual states (D65.2): a file the game leaves out, a file nobody can read, and a folder that is
// missing now or comes back in an odd state. In every case no file the player has may be lost.
public class ProfileStartEmptyUnusualFolderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<FolderCapturePreview> SavesPreviewAsync(LaunchRig rig) =>
        (await rig.Workspace.Games.GetCapturePreviewAsync(rig.Game.Id, Ct)).Value!.Folders.Single(folder => folder.Role == RepoRole.External);

    private static string RefusalOf(OperationResult<JobStarted> refused)
    {
        Assert.False(refused.Ok);
        Assert.Equal("error.validation.invalidPayload", refused.Error!.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(refused.Error.Args["issues"]));
        Assert.Equal("startEmpty", issue.Path);
        return issue.MessageId;
    }

    private static async Task<Profile> ProfileNamedAsync(LaunchRig rig, string name) =>
        (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Name == name);

    [Fact(Timeout = 240_000)]
    public async Task A_saves_file_the_game_leaves_out_does_not_stop_the_saves_folder_starting_empty_and_stays_on_disk()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        await rig.EditGameAsync(game => game with { IgnorePaths = ["*.log"] });
        rig.WriteSave("crash.log", "a file no snapshot holds, because the game leaves it out");

        var preview = await SavesPreviewAsync(rig);
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await rig.WaitForJobAsync(started.Value.JobId);

        Assert.True(preview.HeldInSnapshot);
        Assert.Equal(StartEmptyBlock.None, preview.StartEmptyBlocked);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("a file no snapshot holds, because the game leaves it out", rig.ReadSave("crash.log"));
        Assert.True(rig.SavesHave("slot1.sav"));
    }

    [Fact(Timeout = 240_000)]
    public async Task Switching_to_an_empty_saves_start_removes_the_snapshotted_saves_and_leaves_the_file_the_game_leaves_out()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        await rig.EditGameAsync(game => game with { IgnorePaths = ["*.log"] });
        rig.WriteSave("crash.log", "a file no snapshot holds, because the game leaves it out");
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);
        Assert.True(started.Ok, started.Error?.Detail);
        await rig.WaitForJobAsync(started.Value.JobId);
        var bare = await ProfileNamedAsync(rig, "Bare");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(bare.Id));
        var job = await rig.SwitchAsync(new LaunchTarget.ProfileTip(bare.Id));

        Assert.Equal(1, Assert.IsType<FolderLaunchState>(preview.Folder(RepoRole.External)).Switch!.FilesRemoved);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(rig.SavesHave("slot1.sav"));
        Assert.Equal("a file no snapshot holds, because the game leaves it out", rig.ReadSave("crash.log"));
    }

    [Theory(Timeout = 240_000)]
    [MemberData(nameof(HeldFiles.Kinds), MemberType = typeof(HeldFiles))]
    public async Task A_saves_file_nobody_can_read_stops_the_saves_folder_starting_empty_with_a_reason_a_snapshot_cannot_fix(HeldFileKind kind)
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), RepoRole.External);
        HeldFiles.MakeRacy(new RepoRef(gitDir, rig.SavesPath, rig.Game.Id, RepoRole.External));
        var saves = rig.SnapshotOf(RepoRole.External);

        using (HeldFiles.Hold(Path.Combine(rig.SavesPath, "slot1.sav"), kind))
        {
            var preview = await SavesPreviewAsync(rig);
            var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

            Assert.False(preview.HeldInSnapshot);
            Assert.Equal(StartEmptyBlock.Other, preview.StartEmptyBlocked);
            Assert.Equal(ValidationMessageIds.ProfileStartEmptyUnchecked, RefusalOf(refused));
        }

        Assert.Equal(["Vanilla"], (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Select(profile => profile.Name));
        Assert.Equal(saves, rig.SnapshotOf(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_with_a_stopped_switch_is_refused_with_a_reason_a_snapshot_cannot_fix()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), RepoRole.External);
        File.WriteAllText(SwitchJournal.PathIn(gitDir), "{ this is not a note");

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

        Assert.Equal(ValidationMessageIds.ProfileStartEmptyUnchecked, RefusalOf(refused));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_that_is_missing_loses_nothing_and_the_start_reports_the_folder_as_not_made()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        var away = rig.SavesPath + "-away";
        BackendWorkspace.MoveFolder(rig.SavesPath, away);

        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);

        Assert.True(started.Ok, started.Error?.Detail);
        var result = Assert.IsType<CaptureResult>((await rig.WaitForJobAsync(started.Value.JobId)).Result);
        var saves = result.Folders.Single(folder => folder.Role == RepoRole.External);
        Assert.False(saves.Ok);
        Assert.Equal("error.path.missing", saves.Error!.MessageId);
        Assert.True(result.Folders.Single(folder => folder.Role == RepoRole.Install).Ok);
        Assert.True(File.Exists(Path.Combine(away, "slot1.sav")));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_saves_folder_the_player_made_again_with_their_own_files_is_not_started_empty_by_the_retry_and_keeps_them()
    {
        using var rig = await LaunchRig.CreateAsync();
        await rig.ProfileAsync("Vanilla");
        BackendWorkspace.MoveFolder(rig.SavesPath, rig.SavesPath + "-away");
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Bare", null, "Bare", [RepoRole.External]);
        Assert.True(started.Ok, started.Error?.Detail);
        Assert.False(Assert.IsType<CaptureResult>((await rig.WaitForJobAsync(started.Value.JobId)).Result).Folders.Single(folder => folder.Role == RepoRole.External).Ok);
        rig.WriteSave("mine.sav", "the player's own save in a folder they made again");
        var made = await ProfileNamedAsync(rig, "Bare");

        var retry = await rig.Profiles.CaptureAsync(rig.Game.Id, made.Id, "Bare", [RepoRole.External]);
        Assert.True(retry.Ok, retry.Error?.Detail);
        var folder = Assert.IsType<CaptureResult>((await rig.WaitForJobAsync(retry.Value.JobId)).Result).Folders.Single();
        Assert.False(folder.Ok);
        Assert.Equal("error.validation.invalidPayload", folder.Error!.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(folder.Error.Args["issues"]));
        Assert.Equal(ValidationMessageIds.ProfileStartEmptyUnsaved, issue.MessageId);

        Assert.Equal("the player's own save in a folder they made again", rig.ReadSave("mine.sav"));
    }
}
