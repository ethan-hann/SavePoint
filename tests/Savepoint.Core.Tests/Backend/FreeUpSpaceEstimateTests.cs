using System.Security.Cryptography;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Backend;

// Before Free up space runs, the player is told about how much space it would free: the stored size of what only the snapshots it
// deletes hold. Content a snapshot that stays also holds never counts.
public class FreeUpSpaceEstimateTests
{
    private const int _bigBytes = 300_000;

    // The listings and commits of the removed snapshots add a little to the file's size.
    private const int _slack = 60_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Timeout = 240_000)]
    public async Task A_game_with_nothing_removed_frees_nothing()
    {
        using var world = await RemovedWorld.CreateAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;

        var estimate = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct);

        Assert.Equal(0L, estimate.Value);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_big_file_only_the_removed_snapshots_hold_is_counted_by_about_its_size()
    {
        using var world = await RemovedWorld.CreateAsync();
        var big = await RemoveABigFileAsync(world);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;

        var estimate = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct);

        Assert.True(estimate.Ok, estimate.Error?.Detail);
        Assert.InRange(estimate.Value, big.Length, big.Length + _slack);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_same_file_in_a_snapshot_that_stays_is_not_counted_on_the_next_ask()
    {
        using var world = await RemovedWorld.CreateAsync();
        var big = await RemoveABigFileAsync(world);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var before = (await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct)).Value;

        // The profile takes a new snapshot that holds the same file.
        WriteBig(world, big);
        await RemovedWorld.CaptureAsync(world.Workspace, world.Game, world.Profile, "Kept again", RepoRole.Install);
        var after = (await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct)).Value;

        Assert.InRange(before, big.Length, big.Length + _slack);
        Assert.InRange(after, 0, _slack);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_estimate_is_close_to_the_space_the_run_then_frees()
    {
        using var world = await RemovedWorld.CreateAsync();
        var big = await RemoveABigFileAsync(world);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var estimate = (await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct)).Value;

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        var freed = Assert.Single(Assert.IsType<OptimizeResult>(job.Result).Games).BytesFreed;
        Assert.InRange(freed ?? 0, big.Length * 9 / 10, big.Length + _slack);
        Assert.InRange(estimate, (freed ?? 0) - _slack, (freed ?? 0) + _slack);
    }

    [Fact(Timeout = 240_000)]
    public async Task Every_games_estimate_adds_up_and_a_games_own_estimate_counts_only_its_records()
    {
        using var alpha = await RemovedWorld.CreateAsync();
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        var alphaBig = await RemoveABigFileAsync(alpha);
        var betaBig = await RemoveABigFileAsync(beta);
        var preview = (await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(Ct)).Value!;

        var all = await alpha.Workspace.Settings.EstimateFreeUpSpaceAsync(preview.Token, Ct);
        var justAlpha = await alpha.Workspace.Games.EstimateFreeUpSpaceAsync(alpha.Game.Id, preview.Token, Ct);

        Assert.InRange(all.Value, alphaBig.Length + betaBig.Length, alphaBig.Length + betaBig.Length + (2 * _slack));
        Assert.InRange(justAlpha.Value, alphaBig.Length, alphaBig.Length + _slack);
    }

    // A run that was cut off has its note on disk and finishes first, so the data of the records the note names comes back too, though the
    // preview no longer lists them.
    [Fact(Timeout = 240_000)]
    public async Task Data_of_records_a_cut_off_run_still_owns_is_counted_as_freed()
    {
        using var world = await RemovedWorld.CreateAsync();
        var first = await RemoveABigFileAsync(world);
        var before = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!.Token.Records;
        var second = await RemoveABigFileAsync(world);
        var both = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!.Token.Records;
        var owned = both.Except(before).Select(record => new ClearedRef(record.RefName, record.Value)).ToList();
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(Ct);
        FreeUpIntent.Write(storage.GameDirectory(world.Game.Id), new Dictionary<RepoRole, IReadOnlyList<ClearedRef>> { [RepoRole.Install] = owned });
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;

        var estimate = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct);

        Assert.NotEmpty(owned);
        Assert.InRange(estimate.Value, first.Length + second.Length, first.Length + second.Length + (2 * _slack));
    }

    // The first walk waits for another walk of a different unit (folder or game) to start; a sequential estimate never lets one, so the wait ends.
    [Fact(Timeout = 240_000)]
    public async Task A_games_folders_are_measured_side_by_side()
    {
        var overlap = new SideBySide(gitDir => gitDir);
        using var world = await RemovedWorld.AfterContinueAsync(overlap.Hooks);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        overlap.Armed = true;

        var estimate = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, Ct);

        Assert.True(estimate.Ok, estimate.Error?.Detail);
        Assert.True(overlap.FirstWalkSawAnother);
    }

    [Fact(Timeout = 240_000)]
    public async Task Every_games_are_measured_side_by_side()
    {
        var overlap = new SideBySide(gitDir => Path.GetDirectoryName(gitDir)!);
        using var alpha = await RemovedWorld.CreateAsync(options => options with { Engine = new GitEngineOptions { InvocationObserver = overlap.Hooks.Observe } });
        using var beta = await RemovedWorld.CreateAsync(shared: alpha.Workspace, name: "Beta");
        await RemoveABigFileAsync(alpha);
        await RemoveABigFileAsync(beta);
        var preview = (await alpha.Workspace.Settings.GetFreeUpSpacePreviewAsync(Ct)).Value!;
        overlap.Armed = true;

        var estimate = await alpha.Workspace.Settings.EstimateFreeUpSpaceAsync(preview.Token, Ct);

        Assert.True(estimate.Ok, estimate.Error?.Detail);
        Assert.True(overlap.FirstWalkSawAnother);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_token_no_preview_issued_is_refused()
    {
        using var world = await RemovedWorld.CreateAsync();
        await RemoveABigFileAsync(world);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var forged = new FreeUpSpaceToken(preview.Token.Records, new byte[32]);

        var forGame = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, forged, Ct);
        var forAll = await world.Workspace.Settings.EstimateFreeUpSpaceAsync(forged, Ct);

        Assert.False(forGame.Ok);
        Assert.False(forAll.Ok);
    }

    [Fact(Timeout = 240_000)]
    public async Task An_unknown_game_fails_and_a_cancelled_ask_fails_as_cancelled()
    {
        using var world = await RemovedWorld.CreateAsync();
        await RemoveABigFileAsync(world);
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var unknown = await world.Workspace.Games.EstimateFreeUpSpaceAsync("no-such-game", preview.Token, Ct);
        var stopped = await world.Workspace.Games.EstimateFreeUpSpaceAsync(world.Game.Id, preview.Token, cancelled.Token);

        Assert.False(unknown.Ok);
        Assert.Equal("error.operation.cancelled", stopped.Error?.MessageId);
    }

    // Holds the first listing walk until a walk of another unit starts, or ten seconds pass.
    private sealed class SideBySide
    {
        private readonly Func<string, string> _unitOf;
        private readonly HashSet<string> _units = [];
        private readonly ManualResetEventSlim _another = new();
        private bool? _firstSawAnother;

        public SideBySide(Func<string, string> unitOf)
        {
            _unitOf = unitOf;
            Hooks = new GitHooks { OnGit = Walk };
        }

        public GitHooks Hooks { get; }

        public bool FirstWalkSawAnother => _firstSawAnother == true;

        // Setup runs git too; only the walks after this count.
        public volatile bool Armed;

        private void Walk(GitInvocation invocation)
        {
            if (!Armed || invocation.Arguments[0] != "rev-list" || invocation.GitDir is not { } gitDir)
            {
                return;
            }

            bool first;
            lock (_units)
            {
                _units.Add(_unitOf(gitDir));
                first = _firstSawAnother is null;
                _firstSawAnother ??= false;
                if (_units.Count >= 2)
                {
                    _another.Set();
                }
            }

            var met = _another.Wait(TimeSpan.FromSeconds(10));
            if (first)
            {
                _firstSawAnother = met;
            }
        }
    }

    private static void WriteBig(RemovedWorld world, byte[] content)
    {
        var path = Path.Combine(world.Game.Install.Path, "mods", "big.pak");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    // Adds a big random file in a snapshot of its own and then continues the profile from the first snapshot, which takes that snapshot off it.
    private static async Task<byte[]> RemoveABigFileAsync(RemovedWorld world)
    {
        var big = RandomNumberGenerator.GetBytes(_bigBytes);
        WriteBig(world, big);
        await RemovedWorld.CaptureAsync(world.Workspace, world.Game, world.Profile, "Added a big mod", RepoRole.Install);
        await world.ContinueFromFirstAsync();
        return big;
    }
}
