using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A new profile can start with no saves, no mods, or neither: the folder gets an empty history instead of a snapshot of its files, the
// files on disk stay put, and the next switch empties the folder through the normal launch check (D61, D37.2).
public class ProfileStartEmptyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Vanilla is the game as it stands: an install folder, a saves folder and a mods folder, each with files.
    private static async Task<(LaunchRig Rig, Profile Vanilla)> WithVanillaAsync()
    {
        var rig = await LaunchRig.CreateAsync();
        try
        {
            await rig.AddModsFolderAsync();
            return (rig, await rig.ProfileAsync("Vanilla"));
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    private static async Task<(Profile Profile, CaptureResult Result)> CreateAsync(LaunchRig rig, string name, params RepoRole[] startEmpty)
    {
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, name, null, name, startEmpty);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await rig.WaitForJobAsync(started.Value.JobId);
        var result = Assert.IsType<CaptureResult>(job.Result);
        var listed = await rig.Profiles.ListAsync(rig.Game.Id, Ct);
        return (listed.Value!.Single(profile => profile.Id == result.ProfileId), result);
    }

    // SavePoint's own marker file stays in every tracked folder, so "empty" means nothing else is left.
    private static IEnumerable<string> WithoutMarker(IReadOnlyDictionary<string, string> files) =>
        files.Keys.Where(path => !path.StartsWith(".savepoint-managed.json", StringComparison.Ordinal));

    private static Task<IReadOnlyList<string>> TreeAsync(LaunchRig rig, Profile profile, RepoRole role) =>
        rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, profile.BranchName, Ct), role);

    [Fact(Timeout = 240_000)]
    public async Task An_empty_mods_start_gives_the_mods_folder_an_empty_history_and_snapshots_the_install_and_saves()
    {
        var (rig, vanilla) = await WithVanillaAsync();
        using var _ = rig;

        var (made, result) = await CreateAsync(rig, "Test run", RepoRole.Mods);

        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Equal(3, result.Folders.Count);
        Assert.Empty(await TreeAsync(rig, made, RepoRole.Mods));
        Assert.Single(await rig.CommitsAsync(RepoRole.Mods, made));
        Assert.Contains("game.exe", await TreeAsync(rig, made, RepoRole.Install));
        Assert.Contains("slot1.sav", await TreeAsync(rig, made, RepoRole.External));
        Assert.NotEmpty(await TreeAsync(rig, vanilla, RepoRole.Mods));
    }

    [Fact(Timeout = 240_000)]
    public async Task An_empty_saves_start_gives_the_saves_folder_an_empty_history_and_snapshots_the_install_and_mods()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;

        var (made, result) = await CreateAsync(rig, "Fresh saves", RepoRole.External);

        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Empty(await TreeAsync(rig, made, RepoRole.External));
        Assert.Single(await rig.CommitsAsync(RepoRole.External, made));
        Assert.Contains("game.exe", await TreeAsync(rig, made, RepoRole.Install));
        Assert.Contains("modA/info.xml", await TreeAsync(rig, made, RepoRole.Mods));
    }

    [Fact(Timeout = 240_000)]
    public async Task Starting_both_empty_leaves_only_the_install_snapshotted_and_all_three_folders_with_a_history()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;

        var (made, result) = await CreateAsync(rig, "Bare", RepoRole.External, RepoRole.Mods);

        Assert.All(result.Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Empty(await TreeAsync(rig, made, RepoRole.External));
        Assert.Empty(await TreeAsync(rig, made, RepoRole.Mods));
        Assert.Contains("game.exe", await TreeAsync(rig, made, RepoRole.Install));
        Assert.NotNull(result.PointId);
    }

    [Fact(Timeout = 240_000)]
    public async Task Creating_the_profile_does_not_touch_any_file_on_disk()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;
        var install = rig.SnapshotOf(RepoRole.Install);
        var saves = rig.SnapshotOf(RepoRole.External);
        var mods = rig.ModsSnapshot();

        await CreateAsync(rig, "Bare", RepoRole.External, RepoRole.Mods);

        Assert.Equal(install, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(saves, rig.SnapshotOf(RepoRole.External));
        Assert.Equal(mods, rig.ModsSnapshot());
    }

    [Fact(Timeout = 240_000)]
    public async Task The_install_folder_can_never_start_empty()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Nothing", null, "Nothing", [RepoRole.Install]);

        Assert.False(refused.Ok);
        Assert.Equal("error.validation.invalidPayload", refused.Error!.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(refused.Error.Args["issues"]));
        Assert.Equal(ValidationMessageIds.ProfileStartEmptyInvalid, issue.MessageId);
        Assert.Equal("startEmpty", issue.Path);
        Assert.Equal(["Vanilla"], (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Select(profile => profile.Name));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_folder_the_game_does_not_have_cannot_start_empty()
    {
        using var rig = await LaunchRig.CreateAsync(withSaves: false);

        var refused = await rig.Profiles.CreateAsync(rig.Game.Id, "Nothing", null, "Nothing", [RepoRole.Mods]);

        Assert.False(refused.Ok);
        Assert.Equal("error.validation.invalidPayload", refused.Error!.MessageId);
        Assert.Empty((await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_row_of_a_profile_with_no_mods_counts_zero_mod_files()
    {
        var (rig, vanilla) = await WithVanillaAsync();
        using var _ = rig;
        var (made, _) = await CreateAsync(rig, "Test run", RepoRole.Mods);

        var rows = (await rig.Profiles.GetRowStatsAsync(rig.Game.Id, Ct)).Value!;

        Assert.Equal(0, rows.Single(row => row.ProfileId == made.Id).ModFileCount);
        Assert.True(rows.Single(row => row.ProfileId == vanilla.Id).ModFileCount > 0);
    }

    [Fact(Timeout = 240_000)]
    public async Task Switching_to_a_profile_with_no_mods_names_the_files_it_removes_and_empties_the_folder_only_when_started()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;
        var (made, _) = await CreateAsync(rig, "Test run", RepoRole.Mods);
        var mods = rig.ModsSnapshot();

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(made.Id));

        var folder = Assert.IsType<FolderLaunchState>(preview.Mods);
        Assert.False(folder.MatchesTarget);
        Assert.False(folder.Dirty);
        Assert.Equal(1, folder.Switch!.FilesChanged);
        Assert.Equal(1, folder.Switch.FilesRemoved);
        Assert.True(folder.Switch.BytesRemoved > 0);
        Assert.Equal(0, preview.TargetInstall.ModFileCount);
        Assert.Equal(1, preview.CurrentInstall.ModFileCount);
        Assert.Equal(mods, rig.ModsSnapshot());

        var job = await rig.SwitchAsync(new LaunchTarget.ProfileTip(made.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(WithoutMarker(rig.ModsSnapshot()));
        Assert.True(rig.SavesHave("slot1.sav"));
        Assert.True(rig.InstallHas("game.exe"));
    }

    [Fact(Timeout = 240_000)]
    public async Task Switching_to_a_profile_with_no_saves_empties_the_saves_folder_through_the_launch()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;
        var (made, _) = await CreateAsync(rig, "Fresh saves", RepoRole.External);

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(made.Id));
        var saves = Assert.IsType<FolderLaunchState>(preview.External);
        Assert.False(saves.MatchesTarget);
        Assert.Equal(1, saves.Switch!.FilesChanged);
        Assert.Equal(1, saves.Switch.FilesRemoved);
        Assert.True(rig.SavesHave("slot1.sav"));

        var job = await rig.SwitchAsync(new LaunchTarget.ProfileTip(made.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(WithoutMarker(rig.SnapshotOf(RepoRole.External)));
        Assert.NotEmpty(WithoutMarker(rig.ModsSnapshot()));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_folder_that_failed_to_start_empty_is_started_empty_by_the_retry_not_captured()
    {
        var (rig, _) = await WithVanillaAsync();
        using var _ = rig;
        var away = rig.ModsPath() + "-away";
        BackendWorkspace.MoveFolder(rig.ModsPath(), away);
        var started = await rig.Profiles.CreateAsync(rig.Game.Id, "Test run", null, "Test run", [RepoRole.Mods]);
        Assert.True(started.Ok, started.Error?.Detail);
        var first = Assert.IsType<CaptureResult>((await rig.WaitForJobAsync(started.Value.JobId)).Result);
        Assert.False(first.Folders.Single(folder => folder.Role == RepoRole.Mods).Ok);
        BackendWorkspace.MoveFolder(away, rig.ModsPath());
        var made = (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Name == "Test run");

        var retry = await rig.CaptureAsync(made, "Test run", RepoRole.Mods);

        Assert.Equal(JobStatus.Succeeded, retry.Status);
        Assert.Empty(await TreeAsync(rig, made, RepoRole.Mods));
    }
}
