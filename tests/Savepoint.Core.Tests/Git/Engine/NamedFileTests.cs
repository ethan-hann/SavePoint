using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A file the game's patterns name is outside what SavePoint records or restores, even when an earlier point holds it: it is never
// unsaved work, a save stops recording it, and a discard leaves it as it is on disk.
[Collection(EngineCollections.C)]
public sealed class NamedFileTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // profile-1 holds game.cfg and, recorded before the game's patterns named them, logs/run.log and logs/old.log. The patterns are
    // "logs/", and the folder is on profile-1.
    private async Task<RepoRef> RecordedBeforeThePatternAsync()
    {
        var (repo, _) = await RepoTemplates.CopyAsync<string?>("named-files", _ws, async ws =>
        {
            var repo = await ws.InitializedFreshAsync();
            ws.Write(repo, "game.cfg", "cfg");
            ws.Write(repo, "logs/run.log", "recorded log");
            ws.Write(repo, "logs/old.log", "recorded old log");
            await ws.CaptureAsync(repo, "profile-1", "First");
            return (repo, null);
        });
        return repo with { IgnorePatterns = ["logs/"] };
    }

    [Fact]
    public async Task A_recorded_file_the_patterns_name_is_not_unsaved_work_when_it_changes_or_disappears()
    {
        var repo = await RecordedBeforeThePatternAsync();
        _ws.Write(repo, "logs/run.log", "what the game wrote since");
        _ws.Delete(repo, "logs/old.log");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsClean);
        Assert.Empty(status.ChangedPaths);
        Assert.Empty(status.UntrackedPaths);
    }

    [Fact]
    public async Task A_save_stops_recording_a_file_the_patterns_name_and_leaves_it_on_disk()
    {
        var repo = await RecordedBeforeThePatternAsync();
        _ws.Write(repo, "logs/run.log", "what the game wrote since");
        _ws.Write(repo, "notes.txt", "a new file the save has to record");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Second", ignore: ["logs/"]);

        Assert.True(outcome.Changed);
        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Contains("notes.txt", tree);
        Assert.Contains("game.cfg", tree);
        Assert.DoesNotContain("logs/run.log", tree);
        Assert.DoesNotContain("logs/old.log", tree);
        Assert.Equal("what the game wrote since", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    // Whatever way a save is made, the point it writes stops holding the files the patterns name. An earlier point keeps them, except
    // the one an update replaces: the update is a save like any other.
    [Theory]
    [InlineData(CaptureMode.Amend)]
    [InlineData(CaptureMode.Fork)]
    [InlineData(CaptureMode.Root)]
    [InlineData(CaptureMode.MoveForward)]
    public async Task Every_way_of_saving_stops_recording_a_file_the_patterns_name_and_leaves_it_on_disk(CaptureMode mode)
    {
        var repo = await RecordedBeforeThePatternAsync();
        var first = (await _ws.TipAsync(repo, "profile-1"))!;
        _ws.Write(repo, "logs/run.log", "what the game wrote since");
        _ws.Write(repo, "notes.txt", "a new file the save has to record");
        var branch = mode is CaptureMode.Amend or CaptureMode.MoveForward ? "profile-1" : "profile-2";
        var request = new CaptureRequest(branch, "Second", "cap-2")
        {
            Mode = mode,
            IgnorePatterns = ["logs/"],
            ExpectedStartCommit = mode == CaptureMode.MoveForward ? first : null,
        };

        var outcome = await _ws.Engine.CaptureAsync(repo, request, ct: Ct);

        Assert.True(outcome.Changed);
        var tree = await _ws.TreeAsync(repo, outcome.TipCommitId!);
        Assert.Contains("notes.txt", tree);
        Assert.Contains("game.cfg", tree);
        Assert.DoesNotContain("logs/run.log", tree);
        Assert.DoesNotContain("logs/old.log", tree);
        Assert.Equal("what the game wrote since", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
        if (mode != CaptureMode.Amend)
        {
            Assert.Contains("logs/run.log", await _ws.TreeAsync(repo, first));
        }
    }
    [Fact]
    public async Task A_save_with_nothing_else_to_record_writes_no_point_only_to_drop_files_the_patterns_name()
    {
        var repo = await RecordedBeforeThePatternAsync();
        _ws.Write(repo, "logs/run.log", "what the game wrote since");
        var before = await _ws.TipAsync(repo, "profile-1");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Second", ignore: ["logs/"]);

        Assert.False(outcome.Changed);
        Assert.Equal(before, await _ws.TipAsync(repo, "profile-1"));
        Assert.Contains("logs/run.log", await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_discard_leaves_the_files_the_patterns_name_as_they_are_and_restores_the_rest()
    {
        var repo = await RecordedBeforeThePatternAsync();
        _ws.Write(repo, "logs/run.log", "what the game wrote since");
        _ws.Delete(repo, "logs/old.log");
        _ws.Write(repo, "logs/new.log", "a new file the patterns name");
        _ws.Write(repo, "game.cfg", "edited");
        _ws.Write(repo, "stray.txt", "new");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.Equal("cfg", await File.ReadAllTextAsync(_ws.FileOf(repo, "game.cfg"), Ct));
        Assert.False(File.Exists(_ws.FileOf(repo, "stray.txt")));
        Assert.Equal("what the game wrote since", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/run.log"), Ct));
        Assert.False(File.Exists(_ws.FileOf(repo, "logs/old.log")));
        Assert.Equal("a new file the patterns name", await File.ReadAllTextAsync(_ws.FileOf(repo, "logs/new.log"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task A_capture_preview_leaves_out_the_recorded_files_the_patterns_name()
    {
        var repo = await RecordedBeforeThePatternAsync();

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, ["logs/"], Ct);

        Assert.Equal(["game.cfg"], candidates.Select(candidate => candidate.Path).ToArray());
    }
}
