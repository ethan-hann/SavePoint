using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// What moving the snapshot folder writes into its details: where from and to, each stage, and what stays behind.
public class RelocationLinesTests
{
    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    [Fact]
    public async Task A_move_names_both_folders_and_the_size_then_each_stage_in_order()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");
        var preview = (await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token)).Value!;

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.RelocateStart, ("from", workspace.DefaultStorageRoot), ("to", destination), ("files", preview.FileCount), ("bytes", preview.BytesToCopy)),
                JobLine.ForStep(JobSteps.RelocateCopied),
                JobLine.ForStep(JobSteps.RelocateSwitched, ("path", destination)),
                JobLine.ForStep(JobSteps.RelocateRemovingOld),
            ],
            Steps(job));
    }

    [Fact]
    public async Task An_old_copy_that_stays_is_named_in_a_step_and_not_as_a_bare_path()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();

        // A folder shaped like a game's storage whose "git directory" holds something that git never writes stays where it is.
        var foreign = Path.Combine(workspace.DefaultStorageRoot, "foreign", "install.git");
        BackendWorkspace.WriteFile(Path.Combine(foreign, "family-photos.zip"), "somebody's files");
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.RelocateNotRemoved, ("path", foreign)), Steps(job));
        Assert.DoesNotContain(JobLine.ForPath(foreign), job.RecentLines);
    }

    [Fact]
    public async Task A_move_that_fails_says_it_is_taking_the_copy_back()
    {
        using var workspace = new BackendWorkspace(options => options with { AfterRelocationCopy = (_, _) => throw new IOException("the disk went away") });
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.RelocateTakingBack, ("path", destination)), Steps(job));
        Assert.DoesNotContain(Steps(job), step => step.MessageId is JobSteps.RelocateCopied or JobSteps.RelocateSwitched);
    }
}
