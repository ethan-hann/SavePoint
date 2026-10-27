using Savepoint.Core.Api;
using Savepoint.Core.Tests.Launch.Orchestration;

namespace Savepoint.Core.Tests.Backend;

// The catalog lint cannot see message arguments, so this drives each kind of job against the real engine and reads them.
public class EveryJobKindHidesEngineFoldersTests
{
    [Fact(Timeout = 240_000)]
    public async Task No_kind_of_job_shows_a_git_folder_in_any_line_it_writes()
    {
        // A threshold of zero makes every snapshot queue the routine packing of the game's files.
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync(options => options with { LooseObjectThreshold = 0 }, gameArguments: ["--exit-after", "800"]);
        using var _ = rig;
        var workspace = rig.Workspace;
        var seen = new Dictionary<JobKind, List<JobSnapshot>>();

        void See(JobSnapshot job)
        {
            Assert.NotEqual(JobStatus.Running, job.Status);
            if (!seen.TryGetValue(job.Kind, out var jobs))
            {
                seen[job.Kind] = jobs = [];
            }

            jobs.Add(job);
        }

        // Taking a snapshot of both folders after they changed.
        rig.WriteInstall("mods/c.txt", "mod c");
        rig.WriteSave("slot3.sav", "third save");
        See(await rig.CaptureAsync(modded, "More changes"));
        await workspace.WaitForIdleAsync();

        // Freeing up space, for one game and for all of them.
        var oneGame = await workspace.Games.OptimizeStorageAsync(rig.Game.Id);
        See(await workspace.WaitForJobAsync(oneGame.Value!.JobId));
        var everyGame = await workspace.Settings.OptimizeAllStorageAsync();
        See(await workspace.WaitForJobAsync(everyGame.Value!.JobId));

        // Switching without playing, then playing.
        See(await rig.SwitchAsync(new LaunchTarget.ProfileTip(vanilla.Id)));
        See(await rig.StartAsync(new LaunchTarget.ProfileTip(modded.Id)));
        await rig.WaitForAsync<SessionState.Exited>();
        await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"));
        await rig.WaitForAsync<SessionState.Completed>();
        await workspace.WaitForIdleAsync();

        // Moving the snapshot folder, then removing everything.
        var moved = await workspace.Settings.RelocateStorageAsync(Path.Combine(workspace.Root, "new-storage"));
        See(await workspace.WaitForJobAsync(moved.Value!.JobId));
        var cleared = await workspace.Settings.ClearAllDataAsync();
        See(await workspace.WaitForJobAsync(cleared.Value!.JobId));

        // Every job the backend ran counts, including the ones a launch queued behind the caller's back.
        foreach (var job in workspace.Jobs.List().Where(job => job.Status != JobStatus.Running))
        {
            if (!seen.TryGetValue(job.Kind, out var jobs) || jobs.All(known => known.JobId != job.JobId))
            {
                See(job);
            }
        }

        var uncovered = Enum.GetValues<JobKind>().Where(kind => !seen.ContainsKey(kind)).ToList();
        Assert.True(uncovered.Count == 0, $"no job of these kinds was driven: {string.Join(", ", uncovered)}");
        foreach (var (kind, jobs) in seen)
        {
            foreach (var job in jobs)
            {
                Assert.NotEmpty(job.RecentLines);
                var leaks = EngineFolderLint.Leaks(job);
                Assert.True(leaks.Count == 0, $"a {kind} job shows the engine's folders: {string.Join(" | ", leaks)}");
            }
        }
    }
}
