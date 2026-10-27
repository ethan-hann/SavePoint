using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Git;
using Savepoint.Core.Tests.Services;
using Savepoint.Core.Tests.Services.Exe;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

// The startup check removes the lock files a stopped SavePoint left, on the grounds that nothing of SavePoint's is running (D18). A git that
// outlived that SavePoint still uses them, so the gits its note names are ended first, and when one cannot be the locks stay.
public sealed class StartupLeftoverGitTests : IDisposable
{
    private static readonly TimeSpan _short = TimeSpan.FromMilliseconds(400);

    // The backend a test replaces keeps its own note of running git in the same folder, from a timer after each git starts or ends. On a slow
    // machine that timer fires after the test wrote the note of a stopped SavePoint and replaces or removes it, so the test stops it.
    private static readonly TimeSpan _noNoteMeanwhile = TimeSpan.FromHours(1);

    // A program that does not exist: the SavePoint that wrote the note is gone.
    private static readonly ProcessStamp _deadOwner = new(int.MaxValue - 7, 1);

    private readonly TempFolder _temp = new("savepoint-startup-leftover-");
    private readonly RecordingDiagnosticLog _log = new();
    private readonly List<FakeGameProcess> _games = [];

    public void Dispose()
    {
        foreach (var game in _games)
        {
            game.Dispose();
        }

        _temp.Dispose();
    }

    [Fact(Timeout = 120_000)]
    public async Task A_git_the_note_of_a_stopped_SavePoint_names_is_ended_before_its_locks_are_removed()
    {
        using var workspace = new BackendWorkspace(options => options with { LeftoverWait = _short, RunningGitNoteDelay = _noNoteMeanwhile });
        var gitDir = await GitDirAsync(workspace);
        var leftover = StartLeftover(workspace);
        var lockFile = Path.Combine(gitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, "held by a git that is still running", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(FakeGameProcess.IsRunning(leftover.Id), "the leftover git is still running");
        Assert.False(File.Exists(lockFile), "the lock it held was not removed once it was gone");
    }

    [Fact(Timeout = 120_000)]
    public async Task Locks_and_temporary_files_stay_while_a_leftover_git_could_not_be_ended()
    {
        using var workspace = new BackendWorkspace(options => options with { Leftovers = Unendable() });
        var gitDir = await GitDirAsync(workspace);
        var leftover = StartLeftover(UnendableNote);
        var lockFile = Path.Combine(gitDir, "index.lock");
        var temporaryPack = Path.Combine(gitDir, "objects", "pack", "tmp_pack_left_behind");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPack)!);
        await File.WriteAllTextAsync(lockFile, "held by a git that is still running", workspace.Token);
        await File.WriteAllTextAsync(temporaryPack, "half a pack", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(FakeGameProcess.IsRunning(leftover.Id));
        Assert.True(File.Exists(lockFile), "a lock was removed under a git that is still running");
        Assert.True(File.Exists(temporaryPack), "a temporary pack was removed under a git that is still running");
        Assert.NotEmpty(_log.Containing("could not be ended"));
    }

    [Fact(Timeout = 120_000)]
    public async Task The_screen_is_told_a_task_started_before_SavePoint_closed_is_being_waited_for()
    {
        using var workspace = new BackendWorkspace(options => options with { LeftoverWait = _short, RunningGitNoteDelay = _noNoteMeanwhile });
        await GitDirAsync(workspace);
        StartLeftover(workspace);
        workspace.Restart();
        var steps = new List<StartupProgress>();

        await workspace.AppStatus.CheckAsync(workspace.Token, new SyncProgress(steps.Add));

        Assert.Equal([StartupProgress.WaitingForEarlierTask], steps);
    }

    [Fact(Timeout = 120_000)]
    public async Task Locks_are_removed_as_before_when_no_git_was_left()
    {
        using var workspace = new BackendWorkspace();
        var gitDir = await GitDirAsync(workspace);
        var lockFile = Path.Combine(gitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, "left by a killed git", workspace.Token);
        workspace.Restart();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(lockFile));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_clear_that_was_cut_off_is_not_finished_while_a_leftover_git_could_not_be_ended_and_the_log_says_so()
    {
        using var world = await RemovedWorld.CreateAsync(options => options with { Leftovers = Unendable(), Log = _log });
        var gameDirectory = await WriteFreeUpNoteAsync(world);
        StartLeftover(UnendableNote);
        world.Workspace.Restart();

        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(File.Exists(FreeUpIntent.PathIn(gameDirectory)), "a clear was finished under a git that is still running");
        Assert.NotEmpty(_log.Containing("free-up", "could not be ended"));
    }

    [Fact(Timeout = 240_000)]
    public async Task Nothing_is_logged_about_a_free_up_when_none_was_cut_off()
    {
        using var world = await RemovedWorld.CreateAsync(options => options with { Leftovers = Unendable(), Log = _log });
        StartLeftover(UnendableNote);
        world.Workspace.Restart();

        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.Empty(_log.Containing("free-up"));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_start_waits_for_a_noted_reclaim_first_then_ends_the_leftovers_then_removes_the_locks()
    {
        using var workspace = new BackendWorkspace(options => options with { LeftoverWait = _short, RunningGitNoteDelay = _noNoteMeanwhile });
        var gitDir = await GitDirAsync(workspace);
        var runner = workspace.Backend.Services.Runner;

        // A reclaim git the note of a killed reclaim names, still running, and a leftover the note of running git names.
        var gone = await LiveOrphan.EndedAsync(runner, gitDir);
        var reclaim = await LiveOrphan.StartAsync(runner, gitDir);
        ReclaimMarker.Write(gitDir, gone, reclaim.Stamp);
        var leftover = StartLeftover(workspace);
        var lockFile = Path.Combine(gitDir, "index.lock");
        await File.WriteAllTextAsync(lockFile, "left by a killed git", workspace.Token);

        workspace.Restart();

        var check = workspace.AppStatus.CheckAsync(workspace.Token);

        // Five times the leftovers' wait, which the check must outlast without ending the reclaim: it is waited for, and nothing after it has begun.
        await Task.Delay(_short * 5, workspace.Token);
        var reclaimStillRunning = FakeGameProcess.IsRunning(reclaim.Stamp.Id);
        var leftoverBeforeReclaimEnded = FakeGameProcess.IsRunning(leftover.Id);
        var lockBeforeReclaimEnded = File.Exists(lockFile);
        var checkBeforeReclaimEnded = check.IsCompleted;
        await reclaim.EndAsync();
        var status = await check.WaitAsync(TimeSpan.FromSeconds(60), workspace.Token);

        Assert.True(reclaimStillRunning, "the git of a noted reclaim was ended instead of waited for");
        Assert.True(leftoverBeforeReclaimEnded, "the leftovers were dealt with before the reclaim was settled");
        Assert.True(lockBeforeReclaimEnded, "the locks were removed before the reclaim was settled");
        Assert.False(checkBeforeReclaimEnded);
        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(FakeGameProcess.IsRunning(leftover.Id));
        Assert.False(File.Exists(lockFile));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_git_the_backend_runs_is_written_to_the_note_in_the_runtime_folder_and_the_note_goes_when_it_ends()
    {
        using var workspace = new BackendWorkspace(options => options with { RunningGitNoteDelay = TimeSpan.FromMilliseconds(100) });
        var gitDir = await GitDirAsync(workspace);
        var notePath = RunningGitNote.PathIn(workspace.Platform.Directories.RuntimeDir);

        var orphan = await LiveOrphan.StartAsync(workspace.Backend.Services.Runner, gitDir);
        var written = await FakeGameProcess.WaitUntilAsync(() => RunningGitNote.Read(notePath)?.Gits.Contains(orphan.Stamp) == true, TimeSpan.FromSeconds(10));
        var owner = RunningGitNote.Read(notePath)?.Owner;
        await orphan.EndAsync();
        var cleared = await FakeGameProcess.WaitUntilAsync(() => !File.Exists(notePath), TimeSpan.FromSeconds(10));

        Assert.True(written, "the running git was never written to the note");
        Assert.Equal(ProcessStamp.Current(), owner);
        Assert.True(cleared, "the note stayed after the git ended");
    }

    private static async Task<string> GitDirAsync(BackendWorkspace workspace)
    {
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        return storage.GitDirectory(game.Id, RepoRole.Install);
    }

    private string UnendableNote => RunningGitNote.PathIn(_temp.Path);

    // A running program written into a note as the git of a SavePoint that is gone.
    private FakeGameProcess StartLeftover(string notePath)
    {
        var game = FakeGameProcess.Start();
        _games.Add(game);
        using var process = Process.GetProcessById(game.Id);
        RunningGitNote.Write(notePath, _deadOwner, [ProcessStamp.Of(process)!.Value]);
        return game;
    }

    private FakeGameProcess StartLeftover(BackendWorkspace workspace) => StartLeftover(RunningGitNote.PathIn(workspace.Platform.Directories.RuntimeDir));

    // Built again by every build of the backend, so it reads the note as it is then; its kill does nothing.
    private LeftoverGits Unendable() => new(UnendableNote, _log, null, _short, _short, TimeSpan.FromMilliseconds(50), _ => { });

    private static async Task<string> WriteFreeUpNoteAsync(RemovedWorld world)
    {
        var token = world.Workspace.Token;
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, token)).Value!;
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(token);
        var plans = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            var names = preview.Token.Records.Where(record => record.Folder == role).Select(record => record.RefName).ToList();
            plans[role] = await storage.Engine.FindClearableAsync(world.RepoOf(storage, role), names, token);
        }

        var gameDirectory = storage.GameDirectory(world.Game.Id);
        FreeUpIntent.Write(gameDirectory, plans);
        return gameDirectory;
    }

    private sealed class SyncProgress(Action<StartupProgress> report) : IProgress<StartupProgress>
    {
        public void Report(StartupProgress value) => report(value);
    }
}
