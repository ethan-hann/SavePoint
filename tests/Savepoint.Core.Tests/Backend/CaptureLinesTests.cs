using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// What a snapshot writes into its details: the profile, each folder it copies, and how many files each added.
public class CaptureLinesTests
{
    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    [Fact]
    public async Task A_snapshot_names_the_profile_each_folder_it_copies_and_the_files_each_added()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "patched");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the patch");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var steps = Steps(job);
        Assert.Equal(JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Vanilla"), ("snapshot", "After the patch")), steps.FirstOrDefault());
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", Path.GetFullPath(game.Install.Path))), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolder, ("folder", RepoRole.External), ("path", Path.GetFullPath(game.External.Path))), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 1)), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.External), ("files", 1)), steps);
    }

    [Fact]
    public async Task A_folder_with_nothing_new_says_it_added_no_files()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Nothing changed");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var steps = Steps(job);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 0)), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderDone, ("folder", RepoRole.External), ("files", 0)), steps);
    }

    [Fact]
    public async Task A_folder_that_could_not_be_copied_says_so()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        await workspace.CreateProfileAsync(game, "Modded");

        // The folders are on the other profile now, so a snapshot into the first one cannot be taken.
        var started = await workspace.Profiles.CaptureAsync(game.Id, first.Id, "Into the wrong one");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var steps = Steps(job);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderFailed, ("folder", RepoRole.Install)), steps);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderFailed, ("folder", RepoRole.External)), steps);
        Assert.DoesNotContain(steps, step => step.MessageId == JobSteps.CaptureFolderDone);
    }

    [Fact]
    public async Task A_snapshot_names_its_profile_in_the_job_while_it_runs_and_after()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Nothing changed");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(new JobProfile(profile.Id, "Vanilla"), job.Profile);
    }

    [Fact]
    public async Task Creating_a_profile_writes_the_same_steps_with_the_new_profiles_name()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));

        var (_, job) = await workspace.CreateProfileAsync(game, "Modded 1.6");

        Assert.Equal(JobLine.ForStep(JobSteps.CaptureStart, ("profile", "Modded 1.6"), ("snapshot", "Modded 1.6")), Steps(job).FirstOrDefault());
        Assert.Equal("Modded 1.6", job.Profile?.Name);
        Assert.Contains(Steps(job), step => step.MessageId == JobSteps.CaptureFolder && Equals(step.Args["folder"], "install"));
    }
}
