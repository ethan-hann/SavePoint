using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

/// <summary>
/// Judges the folders of one save together. They are saved side by side into one storage volume and each capture checks only its own files,
/// so each one reports what it would add once it has found room for that alone, and waits until every folder has either done the same or
/// dropped out (it has nothing to write, or it was refused). The folders that reported are then asked of the volume together. A folder that
/// cannot fit alone never reports, so the others still run and refuse or succeed on their own figures.
/// </summary>
internal sealed class CaptureRoomGate
{
    private readonly GitEngine _engine;
    private readonly IReadOnlyDictionary<RepoRole, RepoRef> _folders;
    private readonly HashSet<RepoRole> _undecided;
    private readonly Dictionary<RepoRole, long> _reported = [];
    private readonly TaskCompletionSource _verdict = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    /// <summary>Starts a gate over the folders of one save.</summary>
    /// <param name="engine">Asks the storage volume.</param>
    /// <param name="folders">Every folder the save will try to write.</param>
    public CaptureRoomGate(GitEngine engine, IReadOnlyList<RepoRef> folders)
    {
        _engine = engine;
        _folders = folders.ToDictionary(folder => folder.Role);
        _undecided = [.. _folders.Keys];
    }

    /// <summary>Reports what a folder would add and waits for the verdict on all the folders that reported.</summary>
    /// <param name="role">The folder.</param>
    /// <param name="newBytes">The bytes its capture would add.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <exception cref="DiskSpaceException">The folders that reported cannot fit together.</exception>
    public Task ReportAsync(RepoRole role, long newBytes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_undecided.Remove(role))
            {
                _reported[role] = newBytes;
                JudgeWhenDecided();
            }
        }

        return _verdict.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Says a folder will not report: it has nothing to write, it was refused alone, or it failed. Safe to call more than once.</summary>
    /// <param name="role">The folder.</param>
    public void DropOut(RepoRole role)
    {
        lock (_gate)
        {
            if (_undecided.Remove(role))
            {
                JudgeWhenDecided();
            }
        }
    }

    // Called with the lock held, after a folder was decided.
    private void JudgeWhenDecided()
    {
        if (_undecided.Count > 0)
        {
            return;
        }

        try
        {
            if (_reported.Count > 1)
            {
                _engine.RequireCaptureRoom([.. _reported.Keys.Select(role => _folders[role])], _reported.Values.Sum());
            }

            _verdict.TrySetResult();
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
            _verdict.TrySetException(e);
        }
    }
}
