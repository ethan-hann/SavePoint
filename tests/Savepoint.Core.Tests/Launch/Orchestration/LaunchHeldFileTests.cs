using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A file git cannot read is listed as a change nobody made. A folder whose only changes are such files counts as clean where a switch
// reads it, and when a switch has to read the file the launch says which file and asks to try again, with nothing switched. A
// save cannot tell such a file from a changed one, so it never skips the folder as clean.
public class LaunchHeldFileTests
{
    public static TheoryData<HeldFileKind> Kinds => HeldFiles.Kinds;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // The folders end on Modded, whose data/base.pak differs from Vanilla's, so going to Vanilla has to read and replace that file.
    private static string HeldFile(LaunchRig rig) => Path.Combine(rig.InstallPath, "data", "base.pak");

    private static Task MakeRacyAsync(LaunchRig rig, RepoRole role = RepoRole.Install) =>
        rig.WithEngineAsync(
            (_, repo) =>
            {
                HeldFiles.MakeRacy(repo);
                return Task.FromResult(true);
            },
            role);

    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task The_preview_names_a_file_that_stands_in_the_way_of_the_switch_instead_of_offering_to_keep_or_discard_it(HeldFileKind kind)
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await MakeRacyAsync(rig);

        using (HeldFiles.Hold(HeldFile(rig), kind))
        {
            var toVanilla = await rig.PreviewAsync(Tip(vanilla));
            var staying = await rig.PreviewAsync(Tip(modded));

            Assert.False(toVanilla.Install.Dirty);
            Assert.Equal(0, toVanilla.Install.DirtyFileCount);
            Assert.False(toVanilla.Install.MatchesTarget);
            Assert.Equal(HeldFiles.ErrorIdOf(kind), toVanilla.Install.HeldBy?.MessageId);
            Assert.Equal(HeldFile(rig), toVanilla.Install.HeldBy!.Args["path"]);
            Assert.Null(toVanilla.External!.HeldBy);

            // A folder that needs no switch has nothing to read: it is clean, and the launch carries on with it.
            Assert.True(staying.Install.MatchesTarget);
            Assert.Null(staying.Install.HeldBy);
            Assert.True(staying.ShowBanner);
        }
    }

    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task A_launch_stops_for_such_a_file_before_it_switches_either_folder(HeldFileKind kind)
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await MakeRacyAsync(rig, RepoRole.External);

        // The saves folder is switched second, so a stop there must come before the install folder is touched.
        var held = Path.Combine(rig.SavesPath, "slot2.sav");
        JobSnapshot job;
        using (HeldFiles.Hold(held, kind))
        {
            job = await rig.StartAsync(Tip(vanilla));
        }

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(HeldFiles.ErrorIdOf(kind), job.Error?.MessageId);
        Assert.Equal(held, job.Error!.Args["path"]);
        Assert.True(rig.InstallHas("mods/a.txt"));
        Assert.Equal("a longer, patched base content", rig.ReadInstall("data/base.pak"));
    }

    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task A_profile_whose_only_change_is_such_a_file_has_no_unsaved_changes_in_its_row(HeldFileKind kind)
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await MakeRacyAsync(rig);

        using (HeldFiles.Hold(HeldFile(rig), kind))
        {
            var stats = (await rig.Profiles.GetRowStatsAsync(rig.Game.Id, rig.Token)).Value!;

            var row = stats.Single(stat => stat.ProfileId == modded.Id);
            Assert.True(row.IsActive);
            Assert.Empty(row.UnsavedChanges);
        }
    }

    // A file nobody changed is listed by git when its stamp cannot be trusted, and a save cannot tell it from a changed one: the update
    // fails and names the file until the other program lets go, and the point is not touched.
    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task After_play_an_unchanged_file_that_git_lists_and_nobody_can_read_fails_an_update_until_it_is_let_go(HeldFileKind kind)
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();
        await MakeRacyAsync(rig);
        var before = (await rig.CommitsAsync(RepoRole.Install, modded)).Select(commit => commit.Id).ToArray();

        SessionState.Completed done;
        using (HeldFiles.Hold(HeldFile(rig), kind))
        {
            Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
            await rig.WaitForAsync<SessionState.Exited>();
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
            done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        }

        var install = Assert.IsType<CaptureResult>(done.Result).Folders.Single(folder => folder.Role == RepoRole.Install);
        Assert.Equal(HeldFiles.ErrorIdOf(kind), install.Error?.MessageId);
        Assert.Equal(HeldFile(rig), install.Error!.Args["path"]);
        Assert.Equal(before, (await rig.CommitsAsync(RepoRole.Install, modded)).Select(commit => commit.Id));

        // Let go, the retry that already exists saves it, and a folder with nothing to record keeps its point.
        Assert.True((await rig.Launch.RetryCompletionAsync(rig.Game.Id)).Ok);
        var retried = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync(session => session.State is SessionState.Completed completed && completed != done, "the retried save")).State);
        Assert.All(Assert.IsType<CaptureResult>(retried.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Equal(before, (await rig.CommitsAsync(RepoRole.Install, modded)).Select(commit => commit.Id));
    }
    // "Update existing" runs on a folder it cannot tell is unchanged, and the other program can let go before the files are added.
    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task An_update_whose_held_file_is_let_go_before_the_files_are_added_leaves_the_point_as_it_was(HeldFileKind kind)
    {
        IDisposable? hold = null;
        using var rig = await LaunchRig.CreateAsync(
            options => options with
            {
                Engine = new GitEngineOptions
                {
                    InvocationObserver = invocation =>
                    {
                        if (invocation.Arguments[0] == "add" && invocation.GitDir!.EndsWith("install.git", StringComparison.Ordinal))
                        {
                            Interlocked.Exchange(ref hold, null)?.Dispose();
                        }
                    },
                },
            },
            gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        var before = Assert.Single(await rig.CommitsAsync(RepoRole.Install, vanilla));
        await rig.StartAsync(Tip(vanilla));

        // The file is held when the game closes, so the folder counts as having something unsaved and a replacement is offered.
        await MakeRacyAsync(rig);
        Volatile.Write(ref hold, HeldFiles.Hold(HeldFile(rig), kind));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.Contains(CompletionChoice.UpdateExisting, exited.Options.Choices);

        SessionState.Completed done;
        try
        {
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
            done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        }
        finally
        {
            Interlocked.Exchange(ref hold, null)?.Dispose();
        }

        var install = Assert.IsType<CaptureResult>(done.Result).Folders.Single(folder => folder.Role == RepoRole.Install);
        Assert.True(install.Ok, install.Error?.Detail);
        Assert.Equal(before.Id, Assert.Single(await rig.CommitsAsync(RepoRole.Install, vanilla)).Id);
    }

    [Theory(Timeout = 150_000)]
    [MemberData(nameof(Kinds))]
    public async Task After_play_a_changed_file_nobody_can_read_is_counted_and_an_update_names_it_instead_of_reporting_success(HeldFileKind kind)
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();

        // The game rewrote a recorded file with content of another length, and a program still holds it.
        rig.WriteInstall("data/base.pak", "what the game wrote this time, which is longer than before");
        SessionState.Exited exited;
        SessionState.Completed done;
        using (HeldFiles.Hold(HeldFile(rig), kind))
        {
            Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
            exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.UpdateExisting("Updated"))).Ok);
            done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        }

        // The file counts as a change, so the dialog never says nothing changed, and the update is not reported as saved.
        Assert.Equal(1, exited.ChangedInstallFileCount);
        var folders = Assert.IsType<CaptureResult>(done.Result).Folders;
        var install = folders.Single(folder => folder.Role == RepoRole.Install);
        Assert.Equal(HeldFiles.ErrorIdOf(kind), install.Error?.MessageId);
        Assert.Equal(HeldFile(rig), install.Error!.Args["path"]);
        Assert.True(folders.Single(folder => folder.Role == RepoRole.External).Ok);

        // Once the file is free, the retry that already exists saves it.
        Assert.True((await rig.Launch.RetryCompletionAsync(rig.Game.Id)).Ok);
        var retried = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync(session => session.State is SessionState.Completed completed && completed != done, "the retried save")).State);
        Assert.All(Assert.IsType<CaptureResult>(retried.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        var after = await rig.StatusAsync(RepoRole.Install);
        Assert.True(after.IsClean);
        Assert.Null(after.HeldFile);
    }
}
