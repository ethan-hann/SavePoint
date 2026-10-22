namespace Savepoint.Core.Tests.Git.Engine;

// The ignore patterns a folder carries with it decide what is unsaved, discarded and cleaned, whatever the last capture wrote (D31).
[Collection(EngineCollections.B)]
public sealed class CurrentPatternsTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Status_leaves_out_what_the_current_patterns_name_without_an_earlier_capture_writing_them()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "keep/save.dat", "screenshot");
        _ws.Write(repo, "other.txt", "new");

        var status = await _ws.Engine.GetStatusAsync(repo with { IgnorePatterns = ["keep/"] }, Ct);

        Assert.Equal(["other.txt"], status.UntrackedPaths);
    }

    [Fact]
    public async Task Discard_and_clean_keep_what_the_current_patterns_name_without_an_earlier_capture_writing_them()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        await _ws.CaptureAsync(repo, "profile-1");
        var current = repo with { IgnorePatterns = ["keep/"] };
        _ws.Write(repo, "keep/save.dat", "screenshot");
        _ws.Write(repo, "other.txt", "new");
        _ws.Write(repo, "a.txt", "edited");

        await _ws.Engine.CleanAsync(current, Ct);

        Assert.True(File.Exists(_ws.FileOf(repo, "keep/save.dat")));
        Assert.False(File.Exists(_ws.FileOf(repo, "other.txt")));
        Assert.Equal("edited", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));

        _ws.Write(repo, "other.txt", "new again");
        await _ws.Engine.DiscardChangesAsync(current, Ct);

        Assert.True(File.Exists(_ws.FileOf(repo, "keep/save.dat")));
        Assert.False(File.Exists(_ws.FileOf(repo, "other.txt")));
        Assert.Equal("one", await File.ReadAllTextAsync(_ws.FileOf(repo, "a.txt"), Ct));
    }
}
