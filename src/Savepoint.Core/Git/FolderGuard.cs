using Savepoint.Core.Diagnostics;
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
    private readonly IDiagnosticLog _log;
    private FolderHold? _hold;

    private FolderGuard(PathSafety safety, RepoRef repo, FolderPin pin, IDiagnosticLog log)
    {
        _safety = safety;
        _repo = repo;
        _pin = pin;
        _log = log;
    }

    // The folder with every link followed; the work tree git is given.
    public string RealPath => _pin.RealPath;

    // Taken after the spelling is judged, so a refused spelling is never probed; the engine judges the pinned folder too when it differs.
    public static FolderGuard Pin(PathSafety safety, RepoRef repo, IDiagnosticLog? log = null) =>
        new(safety, repo, safety.Pin(repo.WorkTree), log ?? DiagnosticLog.None);

    // Holds the folder where the OS allows it and checks that the folder held is the one pinned, then looks at the path once more.
    // What cannot be done is logged on Windows, the one system that can do it, so a report of a swapped folder can be read against it.
    public void Hold(bool hold)
    {
        if (hold)
        {
            _hold = FolderHold.Open(_pin.RealPath, _pin.Identity);
            if (_pin.Identity is { } expected && _hold.Identity is { } actual && actual != expected)
            {
                throw new FolderChangedException(_repo.WorkTree, _repo.Role);
            }

            if (OperatingSystem.IsWindows())
            {
                if (!_hold.IsHeld)
                {
                    _log.Warning($"The {ErrorArgs.NameOf(_repo.Role)} folder '{_repo.WorkTree}' could not be held open, so it can be renamed while SavePoint works on it.");
                }

                if (_pin.Identity is null)
                {
                    _log.Warning($"The {ErrorArgs.NameOf(_repo.Role)} folder '{_repo.WorkTree}' reports no identity, so a swap of the folder can be told only by its path.");
                }
            }
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
