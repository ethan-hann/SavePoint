using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// What the job of a snapshot says about its progress once both folders have been copied.
public class CaptureJobProgressTests
{
    [Fact]
    public async Task A_new_profiles_job_ends_with_every_byte_of_both_folders_counted_as_copied()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var (_, job) = await workspace.CreateProfileAsync(game);

        var progress = job.Progress;
        Assert.True(progress.BytesTotal > 0, "the bytes to copy were never reported");
        Assert.Equal(progress.BytesTotal, progress.BytesDone);
        Assert.Equal(progress.FilesTotal, progress.FilesDone);
    }

    [Fact]
    public async Task Every_file_a_snapshot_copies_is_listed_in_the_details_once()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        for (var i = 0; i < 150; i++)
        {
            File.WriteAllText(Path.Combine(game.Install.Path, "data", $"part{i:D3}.bin"), $"content {i}");
        }

        game = await workspace.AddGameAsync(game);

        var (_, job) = await workspace.CreateProfileAsync(game);

        var listed = job.RecentLines.OfType<JobLine.FilePath>().Select(line => line.Path).ToList();
        var expected = Enumerable.Range(0, 150).Select(i => $"data/part{i:D3}.bin").ToList();
        Assert.Equal(expected, listed.Where(path => path.StartsWith("data/part", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(listed.Count, listed.Distinct().Count());
    }

    [Fact]
    public async Task A_snapshot_with_nothing_new_still_ends_with_a_known_total()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Nothing changed");
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(0, job.Progress.FilesDone);
        Assert.Equal(0, job.Progress.BytesDone);
    }
}
