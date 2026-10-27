using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;
using Savepoint.Core.Tests.Paths;
using Savepoint.Core.Tests.Platform;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Git;

// What the guard says in the log when it cannot do all it is meant to, so a report of a swapped folder can be read against it.
public class FolderGuardTests
{
    private readonly RecordingDiagnosticLog _log = new();

    private static (RepoRef Repo, PathSafety Safety) Folder(TempTree tree, IPathProbe? probe = null, bool exists = true)
    {
        var work = Path.Combine(tree.Root, "games", "alpha");
        if (exists)
        {
            Directory.CreateDirectory(work);
            ConsentMarker.Write(work, "game-1", RepoRole.Install, DateTimeOffset.UtcNow);
        }

        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"), null, probe);
        return (new RepoRef(Path.Combine(tree.Root, "storage", "alpha.git"), work, "game-1", RepoRole.Install), safety);
    }

    [Fact]
    public void A_folder_that_reports_no_identity_is_logged_as_one_whose_swap_only_the_path_can_tell()
    {
        using var tree = new TempTree();
        var (repo, safety) = Folder(tree, new WithoutIdentities());
        using var guard = FolderGuard.Pin(safety, repo, _log);

        guard.Hold(hold: true);

        var line = Assert.Single(_log.Containing("no identity"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(repo.WorkTree, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void On_Windows_a_folder_that_cannot_be_held_open_is_logged()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the guard holds and identifies folders on Windows only");
        using var tree = new TempTree();
        var (repo, safety) = Folder(tree, exists: false);
        using var guard = FolderGuard.Pin(safety, repo, _log);

        // The folder is not there, so the marker check that follows the hold refuses; what the hold said is in the log by then.
        Assert.ThrowsAny<Exception>(() => guard.Hold(hold: true));

        var line = Assert.Single(_log.Containing("could not be held"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(repo.WorkTree, line.Message, StringComparison.Ordinal);
    }

    // Names every folder the same wrong thing, so a recheck by path and identity cannot tell; only the identity of the folder actually held can.
    private sealed class WrongIdentities : IPathProbe
    {
        public string? ReadLinkTarget(string path) => PathProbes.Host.ReadLinkTarget(path);

        public string? ExpandShortName(string path) => PathProbes.Host.ExpandShortName(path);

        public FileIdentity? ReadIdentity(string path) => new FileIdentity(1, 2);

        public IReadOnlyList<string> ReadFinalPaths(string path) => PathProbes.Host.ReadFinalPaths(path);
    }

    [Fact]
    public void On_Windows_a_folder_whose_held_handle_is_another_folder_than_the_pinned_one_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the guard holds and identifies folders on Windows only");
        using var tree = new TempTree();
        var (repo, safety) = Folder(tree, new WrongIdentities());
        using var guard = FolderGuard.Pin(safety, repo, _log);

        var error = Assert.Throws<FolderChangedException>(() => guard.Hold(hold: true));

        Assert.Equal(repo.WorkTree, error.Args["path"]);
    }

    [Fact]
    public void A_folder_that_is_held_and_has_an_identity_logs_nothing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the guard holds and identifies folders on Windows only");
        using var tree = new TempTree();
        var (repo, safety) = Folder(tree);
        using var guard = FolderGuard.Pin(safety, repo, _log);

        guard.Hold(hold: true);

        Assert.Empty(_log.Entries);
    }
}
