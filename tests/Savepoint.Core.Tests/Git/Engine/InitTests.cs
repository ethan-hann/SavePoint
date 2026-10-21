using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class InitTests : IDisposable
{
    private static readonly DateTimeOffset _first = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly FixedClock _clock = new(_first);
    private readonly EngineWorkspace _ws;

    public InitTests()
    {
        _ws = new EngineWorkspace(options => options with { Clock = _clock });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Init_creates_the_git_directory_and_the_folder_above_it()
    {
        var repo = _ws.NewRepo();

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")), "the git directory should hold a HEAD file");
        Assert.True(Directory.Exists(Path.Combine(repo.GitDir, "objects")), "the git directory should hold an object store");
    }

    [Fact]
    public async Task Init_writes_the_consent_marker_for_the_game_and_role()
    {
        var repo = _ws.NewRepo("skyrim-se", RepoRole.External);

        await _ws.Engine.InitAsync(repo, ct: Ct);

        var marker = EngineWorkspace.ReadMarker(repo);
        Assert.NotNull(marker);
        Assert.Equal("skyrim-se", marker.Value.GameId);
        Assert.Equal(RepoRole.External, marker.Value.Role);
        Assert.Equal(_first, marker.Value.ConfirmedAt);
    }

    [Fact]
    public async Task Init_leaves_nothing_but_the_marker_in_the_work_tree()
    {
        var repo = _ws.NewRepo();

        await _ws.Engine.InitAsync(repo, ct: Ct);

        var entries = Directory.GetFileSystemEntries(repo.WorkTree).Select(entry => Path.GetFileName(entry)!).ToArray();
        Assert.Equal([".savepoint-managed.json"], entries);
    }

    [Fact]
    public async Task Init_twice_keeps_the_original_confirmation_time()
    {
        var repo = _ws.NewRepo();
        await _ws.Engine.InitAsync(repo, ct: Ct);
        _clock.Set(_first.AddDays(3));

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal(_first, EngineWorkspace.RequireMarker(repo).ConfirmedAt);
        Assert.True(File.Exists(Path.Combine(repo.GitDir, "HEAD")));
    }

    [Theory]
    [InlineData("other-game", RepoRole.Install)]
    [InlineData("game-1", RepoRole.External)]
    public async Task Init_refuses_a_folder_another_game_or_role_holds_and_creates_no_git_directory(string holderGame, RepoRole holderRole)
    {
        var holder = _ws.NewRepo(holderGame, holderRole);
        await _ws.Engine.InitAsync(holder, ct: Ct);
        var intruder = EngineWorkspace.Claim(holder, "game-1", RepoRole.Install);

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.InitAsync(intruder, ct: Ct));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.False(Directory.Exists(intruder.GitDir), "a refused init must not create the git directory");
        Assert.Equal(holderGame, EngineWorkspace.RequireMarker(holder).GameId);
    }

    [Fact]
    public async Task Init_refuses_a_marker_it_cannot_read_unless_told_to_replace_it()
    {
        var repo = _ws.NewRepo();
        EngineWorkspace.CorruptMarker(repo);

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => _ws.Engine.InitAsync(repo, ct: Ct));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        Assert.Equal("{ not a marker", await File.ReadAllTextAsync(EngineWorkspace.MarkerPath(repo), Ct));
        Assert.False(Directory.Exists(repo.GitDir));

        await _ws.Engine.InitAsync(repo, replaceCorruptMarker: true, ct: Ct);

        Assert.Equal("game-1", EngineWorkspace.RequireMarker(repo).GameId);
    }

    [Theory]
    [InlineData("tier1")]
    [InlineData("tier2")]
    public async Task Init_refuses_a_protected_work_tree_before_writing_anything(string tier)
    {
        var protectedPath = tier == "tier1"
            ? Path.Combine(_ws.StorageRoot, "tracked-here")
            : _ws.Platform.Directories.HomeDir;
        Directory.CreateDirectory(protectedPath);
        var repo = new RepoRef(Path.Combine(_ws.StorageRoot, "other", "install.git"), protectedPath, "game-1", RepoRole.Install);

        var error = await Assert.ThrowsAsync<PathSafetyException>(() => _ws.Engine.InitAsync(repo, ct: Ct));

        Assert.Equal(tier == "tier1" ? "error.pathSafety.tier1Blocked" : "error.pathSafety.tier2Blocked", error.MessageId);
        Assert.False(Directory.Exists(repo.GitDir));
        Assert.False(File.Exists(Path.Combine(protectedPath, ".savepoint-managed.json")));
    }

    [Fact]
    public async Task Init_refuses_a_work_tree_that_does_not_exist_and_does_not_create_it()
    {
        var repo = _ws.NewRepo() with { WorkTree = Path.Combine(_ws.GamesRoot, "moved-away") };

        var error = await Assert.ThrowsAsync<PathMissingException>(() => _ws.Engine.InitAsync(repo, ct: Ct));

        Assert.Equal("error.path.missing", error.MessageId);
        Assert.False(Directory.Exists(repo.WorkTree));
        Assert.False(Directory.Exists(repo.GitDir));
    }

    [Theory]
    [InlineData("inside")]
    [InlineData("around")]
    [InlineData("same")]
    public async Task Init_rejects_a_git_directory_that_overlaps_the_work_tree(string overlap)
    {
        var repo = _ws.NewRepo();
        var gitDir = overlap switch
        {
            "inside" => Path.Combine(repo.WorkTree, ".git-storage"),
            "around" => Path.GetDirectoryName(repo.WorkTree)!,
            _ => repo.WorkTree,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.InitAsync(repo with { GitDir = gitDir }, ct: Ct));

        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Init_rejects_relative_paths()
    {
        var repo = _ws.NewRepo();

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.InitAsync(repo with { WorkTree = "relative/work" }, ct: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.InitAsync(repo with { GitDir = "relative/repo.git" }, ct: Ct));
    }

    [Fact]
    public async Task Init_rejects_a_cancelled_token_without_creating_anything()
    {
        var repo = _ws.NewRepo();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ws.Engine.InitAsync(repo, ct: cancelled.Token));

        Assert.False(Directory.Exists(repo.GitDir));
        Assert.False(File.Exists(EngineWorkspace.MarkerPath(repo)));
    }

    [Fact]
    public async Task Init_survives_a_git_directory_left_half_made_by_an_interrupted_init()
    {
        var repo = _ws.NewRepo();
        Directory.CreateDirectory(Path.Combine(repo.GitDir, "objects"));

        await _ws.Engine.InitAsync(repo, ct: Ct);

        Assert.Equal("savepoint-unborn", await _ws.GitOutAsync(repo, "symbolic-ref", "--short", "HEAD"));
        Assert.Equal("true", await _ws.ConfigAsync(repo, "core.untrackedCache"));
    }
}
