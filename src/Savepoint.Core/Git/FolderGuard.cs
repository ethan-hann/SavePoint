using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Git;

// The game folder as a switch, discard or clean approved it. Git is pointed at the folder the path led to when it was approved,
// not at the path, and Recheck runs right before every git step that writes: it refuses when the path leads somewhere else (a folder
// swapped for a link, renamed away and replaced) or the consent marker is gone, so no later step reaches a folder nobody approved.
// On Windows the folder is also held so it cannot be renamed or deleted while the operation runs.
internal sealed class FolderGuard : IDisposable
{
    private readonly PathSafety _safety;
    private readonly RepoRef _repo;
    private readonly FolderPin _pin;
    private FolderHold? _hold;

    private FolderGuard(PathSafety safety, RepoRef repo, FolderPin pin)
    {
        _safety = safety;
        _repo = repo;
        _pin = pin;
    }

    // The folder with every link followed; the work tree git is given.
    public string RealPath => _pin.RealPath;

    // Taken before the folder is judged, so a swap between the judging and the pin cannot go unseen.
    public static FolderGuard Pin(PathSafety safety, RepoRef repo) => new(safety, repo, safety.Pin(repo.WorkTree));

    // Holds the folder where the OS allows it, then checks that the folder held is the one pinned.
    public void Hold(bool hold)
    {
        if (hold)
        {
            _hold = FolderHold.Open(_pin.RealPath);
        }

        Recheck();
    }

    // Throws FolderChangedException when the path no longer leads to the pinned folder, and the marker's own exception when it is gone.
    public void Recheck()
    {
        if (!_safety.IsStillPinned(_repo.WorkTree, _pin))
        {
            throw new FolderChangedException(_repo.WorkTree, _repo.Role);
        }

        RepoMarker.Verify(_repo);
    }

    public void Dispose() => _hold?.Dispose();
}
