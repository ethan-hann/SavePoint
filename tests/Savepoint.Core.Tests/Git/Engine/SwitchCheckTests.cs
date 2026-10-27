using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Asking whether a switch would be turned away writes nothing, and answers the way the switch itself would.
[Collection(EngineCollections.F)]
public sealed class SwitchCheckTests : IDisposable
{
    private const long _floor = 1_000;

    private readonly EngineWorkspace _ws = new(options => options with { MinFreeBytesForCheckout = _floor });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static bool IsUnder(string path, string root) => path.StartsWith(root, StringComparison.OrdinalIgnoreCase);

    // profile-a holds small.txt; profile-b adds a 300 KB file. The folder is on profile-a.
    private async Task<RepoRef> TwoProfilesAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "small.txt", "x");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "big.bin", new byte[300_000]);
        await _ws.CaptureAsync(repo, "profile-b");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        return repo;
    }

    [Fact]
    public async Task A_switch_the_volume_cannot_take_is_refused_with_the_figures_and_nothing_is_written()
    {
        var repo = await TwoProfilesAsync();
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? _floor + 200_000 : null;
        var before = TreeSnapshot.Take(repo.WorkTree);

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-b", isBranch: true, Ct));

        Assert.Equal(_floor + 300_000, error.Args["requiredBytes"]);
        Assert.Equal(_floor + 200_000, error.Args["freeBytes"]);
        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task A_switch_that_fits_passes_and_leaves_the_folder_and_the_note_alone()
    {
        var repo = await TwoProfilesAsync();
        var before = TreeSnapshot.Take(repo.WorkTree);
        var index = await File.ReadAllBytesAsync(Path.Combine(repo.GitDir, "index"), Ct);
        var config = await File.ReadAllBytesAsync(Path.Combine(repo.GitDir, "config"), Ct);

        await _ws.Engine.CheckSwitchAsync(repo, "profile-b", isBranch: true, Ct);

        Assert.Equal(before, TreeSnapshot.Take(repo.WorkTree));
        Assert.Equal("refs/heads/profile-a", await _ws.HeadRefAsync(repo));
        Assert.Null(SwitchJournal.Read(repo.GitDir));
        Assert.Equal(index, await File.ReadAllBytesAsync(Path.Combine(repo.GitDir, "index"), Ct));
        Assert.Equal(config, await File.ReadAllBytesAsync(Path.Combine(repo.GitDir, "config"), Ct));
    }

    [Fact]
    public async Task A_switch_that_passes_reports_the_bytes_it_adds()
    {
        var repo = await TwoProfilesAsync();

        var growth = await _ws.Engine.CheckSwitchAsync(repo, "profile-b", isBranch: true, Ct);

        Assert.Equal(300_000, growth);
    }

    [Fact]
    public async Task A_switch_that_frees_more_than_it_writes_reports_no_growth()
    {
        var repo = await TwoProfilesAsync();
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-b");

        var growth = await _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct);

        Assert.Equal(0, growth);
    }

    [Fact]
    public async Task Room_for_the_growth_of_several_folders_is_refused_with_the_figures_for_the_volume()
    {
        var repo = await TwoProfilesAsync();
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? _floor + 200_000 : null;

        var error = Assert.Throws<DiskSpaceException>(() => _ws.Engine.RequireSwitchRoom(repo, 250_000));

        Assert.Equal(_floor + 250_000, error.Args["requiredBytes"]);
        Assert.Equal(_floor + 200_000, error.Args["freeBytes"]);
        Assert.Equal(repo.WorkTree, error.Args["path"]);
    }

    [Fact]
    public async Task Room_for_the_growth_of_several_folders_is_granted_when_the_volume_can_take_it()
    {
        var repo = await TwoProfilesAsync();
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? _floor + 250_000 : null;

        _ws.Engine.RequireSwitchRoom(repo, 250_000);
    }

    [Fact]
    public async Task A_half_switched_folder_is_refused_for_another_target_and_passes_for_the_one_it_was_heading_to()
    {
        var repo = await TwoProfilesAsync();
        var a = (await _ws.ResolveAsync(repo, "refs/heads/profile-a"))!;
        var b = (await _ws.ResolveAsync(repo, "refs/heads/profile-b"))!;
        SwitchJournal.Write(repo.GitDir, a, b, "profile-b");

        var error = await Assert.ThrowsAsync<InterruptedSwitchException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-a", isBranch: true, Ct));

        Assert.Equal(b, error.Args["targetPoint"]);
        await _ws.Engine.CheckSwitchAsync(repo, "profile-b", isBranch: true, Ct);
        Assert.NotNull(SwitchJournal.Read(repo.GitDir));
    }

    [Fact]
    public async Task Unsaved_changes_in_the_folder_are_refused()
    {
        var repo = await TwoProfilesAsync();
        _ws.Write(repo, "small.txt", "edited and never saved");

        await Assert.ThrowsAsync<DirtyWorkingTreeException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-b", isBranch: true, Ct));
    }

    [Fact]
    public async Task A_branch_with_no_point_is_refused()
    {
        var repo = await TwoProfilesAsync();

        await Assert.ThrowsAsync<ProfileHasNoPointsException>(() => _ws.Engine.CheckSwitchAsync(repo, "profile-missing", isBranch: true, Ct));
    }

    [Fact]
    public async Task An_older_point_is_checked_by_its_commit_id()
    {
        var repo = await TwoProfilesAsync();
        var older = (await _ws.ResolveAsync(repo, "refs/heads/profile-b"))!;
        _ws.Space.PerPath = path => IsUnder(path, _ws.GamesRoot) ? _floor + 200_000 : null;

        var error = await Assert.ThrowsAsync<DiskSpaceException>(() => _ws.Engine.CheckSwitchAsync(repo, older, isBranch: false, Ct));

        Assert.Equal(_floor + 300_000, error.Args["requiredBytes"]);
    }
}
