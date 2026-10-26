using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A note that a switch started and did not finish is there whether or not it can be read: a capture that could not understand it
// would record a folder that holds files of two points.
[Collection(EngineCollections.C)]
public sealed class UnreadableJournalTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private async Task AssertCaptureRefusedAsync(RepoRef repo, string tip)
    {
        _ws.Write(repo, "c.txt", "a file the capture must not record");

        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.CaptureAsync(repo, "profile-1", "Next"));

        Assert.Equal("error.launch.switchInterrupted", error.MessageId);
        Assert.Equal(RepoRole.Install, error.Args["role"]);
        Assert.Null(error.Args["targetPoint"]);
        Assert.DoesNotContain(_ws.Invocations, invocation => invocation.Arguments.Contains("add") || invocation.Arguments.Contains("commit") || invocation.Arguments.Contains("commit-tree"));
        Assert.Equal(tip, await _ws.TipAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_is_not_valid_json()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{ this is not a note");

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_names_no_target()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        File.WriteAllText(SwitchJournal.PathIn(repo.GitDir), "{\"from\":null}");

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    [Fact]
    public async Task A_capture_refuses_a_folder_whose_switch_note_another_program_holds_open()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows lets one program keep another from reading a file");
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        var note = SwitchJournal.PathIn(repo.GitDir);
        SwitchJournal.Write(repo.GitDir, points[0], points[1]);
        using var held = new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await AssertCaptureRefusedAsync(repo, points[1]);
    }

    [Fact]
    public async Task A_capture_still_works_when_there_is_no_note()
    {
        var (repo, points) = await _ws.TwoPointsRepoAsync();
        _ws.Write(repo, "c.txt", "new");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Next");

        Assert.NotEqual(points[1], outcome.TipCommitId);
    }
}
