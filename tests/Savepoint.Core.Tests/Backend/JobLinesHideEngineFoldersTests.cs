using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// A move and a clear walk the engine's own folders, but what a player reads about them never names those folders.
public class JobLinesHideEngineFoldersTests
{
    private static void AssertNoEngineFolder(JobSnapshot job) => EngineFolderLint.AssertNoEngineFolder(job);

    [Fact]
    public async Task A_move_reports_no_file_path_and_no_engine_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.DoesNotContain(job.RecentLines, line => line is JobLine.FilePath);
        AssertNoEngineFolder(job);
    }

    [Fact]
    public async Task A_move_reports_each_folder_by_game_and_part()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        var folder = job.RecentLines.OfType<JobLine.Step>().FirstOrDefault(step => step.MessageId == JobSteps.RelocateFolder && Equals(step.Args["part"], "install"));
        Assert.NotNull(folder);
        Assert.Equal("Alpha", folder.Args["game"]);
        Assert.True(Convert.ToInt32(folder.Args["files"]) > 0);
    }

    [Fact]
    public async Task A_move_that_leaves_an_old_copy_names_the_game_folder_and_not_the_engine_folder()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var foreign = Path.Combine(workspace.DefaultStorageRoot, "foreign");
        BackendWorkspace.WriteFile(Path.Combine(foreign, "install.git", "family-photos.zip"), "somebody's files");
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.RelocateNotRemoved, ("path", foreign), ("part", "install")), job.RecentLines);
        AssertNoEngineFolder(job);
    }

    [Fact]
    public async Task A_clear_reports_no_engine_folder_even_for_what_it_cannot_remove()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();
        var foreign = Path.Combine(workspace.DefaultStorageRoot, "foreign");
        BackendWorkspace.WriteFile(Path.Combine(foreign, "install.git", "family-photos.zip"), "somebody's files");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Partial, job.Status);
        Assert.Contains(JobLine.ForStep(JobSteps.ClearNotRemoved, ("path", foreign), ("part", "install")), job.RecentLines);
        AssertNoEngineFolder(job);
    }
}
