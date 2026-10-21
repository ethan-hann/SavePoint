using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// profile-1 with three points, the folder on the first, and a change made on top. Every refusal must leave all of it as it was.
public sealed class GuardMatrixFixture : IAsyncLifetime
{
    internal EngineWorkspace Workspace { get; } = new();

    public RepoRef Repo { get; private set; } = null!;

    public string[] Points { get; private set; } = [];

    public SortedDictionary<string, string> FilesBefore { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        Repo = await Workspace.InitializedRepoAsync();
        Points = await Workspace.ThreePointsAsync(Repo);
        await Workspace.GitWriteAsync(Repo, "checkout", "--quiet", "--detach", Points[0]);
        Workspace.Write(Repo, "played.txt", "changes that must survive a refusal");
        FilesBefore = TreeSnapshot.Take(Repo.WorkTree);
    }

    public ValueTask DisposeAsync()
    {
        Workspace.Dispose();
        return ValueTask.CompletedTask;
    }
}

// Every operation that writes to a game folder, or reads from it, checks the folder first: the same three refusals for all of them.
public sealed class GuardMatrixTests : IClassFixture<GuardMatrixFixture>
{
    private readonly GuardMatrixFixture _fx;

    public GuardMatrixTests(GuardMatrixFixture fixture)
    {
        _fx = fixture;
    }

    public static TheoryData<string> Operations =>
    [
        "capture", "capture-fork", "capture-amend", "checkout-branch", "checkout-commit", "force-checkout-branch", "force-checkout-commit",
        "discard", "clean", "create-branch", "create-empty-branch", "delete-branch", "status", "candidates", "estimate",
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GitEngine Engine => _fx.Workspace.Engine;

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_folder_without_a_marker_is_refused_as_missing_and_nothing_changes(string operation)
    {
        var markerText = await File.ReadAllTextAsync(EngineWorkspace.MarkerPath(_fx.Repo), Ct);
        File.Delete(EngineWorkspace.MarkerPath(_fx.Repo));
        try
        {
            var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => Run(operation, _fx.Repo));

            Assert.Equal("error.consentMarker.missing", error.MessageId);
            AssertUntouched();
        }
        finally
        {
            await File.WriteAllTextAsync(EngineWorkspace.MarkerPath(_fx.Repo), markerText, Ct);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_folder_another_game_holds_is_refused_as_a_conflict_and_nothing_changes(string operation)
    {
        var intruder = _fx.Repo with { GameId = "other-game" };

        var error = await Assert.ThrowsAsync<ConsentMarkerException>(() => Run(operation, intruder));

        Assert.Equal("error.consentMarker.conflict", error.MessageId);
        AssertUntouched();
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task A_protected_folder_is_refused_and_nothing_changes(string operation)
    {
        var protectedRepo = _fx.Repo with { WorkTree = _fx.Workspace.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<PathSafetyException>(() => Run(operation, protectedRepo));

        Assert.Empty(Directory.GetFileSystemEntries(protectedRepo.WorkTree));
        AssertUntouched();
    }

    [Fact]
    public async Task Deleting_the_repository_refuses_a_folder_another_game_holds_and_a_protected_one()
    {
        var protectedRepo = _fx.Repo with { WorkTree = _fx.Workspace.Platform.Directories.HomeDir };
        Directory.CreateDirectory(protectedRepo.WorkTree);

        await Assert.ThrowsAsync<ConsentMarkerException>(() => Engine.DeleteRepoAsync(_fx.Repo with { GameId = "other-game" }, Ct));
        await Assert.ThrowsAsync<PathSafetyException>(() => Engine.DeleteRepoAsync(protectedRepo, Ct));

        Assert.True(Directory.Exists(_fx.Repo.GitDir));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(_fx.Repo)));
    }

    // Read from files so the check itself starts no process.
    private void AssertUntouched()
    {
        Assert.Equal(_fx.FilesBefore, TreeSnapshot.Take(_fx.Repo.WorkTree));
        Assert.Equal(_fx.Points[0], GitDirFiles.ReadHead(_fx.Repo.GitDir).DetachedCommit);
        Assert.Equal(["profile-1"], Directory.GetFiles(Path.Combine(_fx.Repo.GitDir, "refs", "heads")).Select(path => Path.GetFileName(path)!));
        Assert.Equal(_fx.Points[2], GitDirFiles.ReadLooseRef(_fx.Repo.GitDir, "refs/heads/profile-1"));
        Assert.False(File.Exists(SwitchJournal.PathIn(_fx.Repo.GitDir)));
    }

    private Task Run(string operation, RepoRef repo) => operation switch
    {
        "capture" => Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "x", "id"), ct: Ct),
        "capture-fork" => Engine.CaptureAsync(repo, new CaptureRequest("profile-fork", "x", "id") { Mode = CaptureMode.Fork, ExpectedStartCommit = _fx.Points[0] }, ct: Ct),
        "capture-amend" => Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "x", "id") { Mode = CaptureMode.Amend }, ct: Ct),
        "checkout-branch" => Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct),
        "checkout-commit" => Engine.CheckoutCommitAsync(repo, _fx.Points[1], ct: Ct),
        "force-checkout-branch" => Engine.ForceCheckoutBranchAsync(repo, "profile-1", Ct),
        "force-checkout-commit" => Engine.ForceCheckoutCommitAsync(repo, _fx.Points[1], Ct),
        "discard" => Engine.DiscardChangesAsync(repo, Ct),
        "clean" => Engine.CleanAsync(repo, Ct),
        "create-branch" => Engine.CreateBranchAsync(repo, "profile-copy", _fx.Points[0], Ct),
        "create-empty-branch" => Engine.CreateEmptyBranchAsync(repo, "profile-empty", ct: Ct),
        "delete-branch" => Engine.DeleteBranchAsync(repo, "profile-1", Ct),
        "status" => Engine.GetStatusAsync(repo, Ct),
        "candidates" => Engine.ListCaptureCandidatesAsync(repo, [], Ct),
        "estimate" => Engine.EstimateNewBytesAsync(repo, [new CaptureCandidate("played.txt", 10)], [], Ct),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
