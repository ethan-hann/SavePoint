namespace Savepoint.Core.Git;

/// <summary>How a capture relates to the history already in the repository.</summary>
public enum CaptureMode
{
    /// <summary>Commits on the checked-out branch, which must be the requested one. A repository with no commits yet starts the branch.</summary>
    Normal,

    /// <summary>Rewrites the tip of the checked-out branch instead of adding a point after it.</summary>
    Amend,

    /// <summary>Starts a new branch at the folder's current commit (older or not), switches to it without touching files, and commits there.</summary>
    Fork,

    /// <summary>Moves an existing branch to the folder's current commit without touching files, switches to it, and commits there.</summary>
    MoveForward,

    /// <summary>
    /// Starts a new branch with a point of its own that has no earlier point behind it, holding what the folder holds now, and switches to
    /// it without touching files. The branch may be started whatever branch the folder is on. Where the repository already has a point, a point is
    /// written even when nothing changed; on a repository with none this is a normal capture, which writes nothing for an empty folder (the caller
    /// then starts the branch itself).
    /// </summary>
    Root,
}

/// <summary>What to capture and how it is recorded.</summary>
public sealed record CaptureRequest
{
    /// <summary>Describes a capture. See each property for what is checked.</summary>
    /// <param name="branch">The profile's branch. For <see cref="CaptureMode.Fork"/> and <see cref="CaptureMode.Root"/> it is the new branch and must not exist yet.</param>
    /// <param name="message">The point's name, stored as the commit subject.</param>
    /// <param name="captureId">Written into the commit as a <c>Savepoint-Capture</c> trailer so the commits of one capture event can be grouped exactly.</param>
    /// <exception cref="ArgumentException">The branch name is not one git accepts, the message is blank, contains NUL or the unit separator character, or has a line that starts like the capture id trailer, or the capture id is empty or contains whitespace.</exception>
    public CaptureRequest(string branch, string message, string captureId)
    {
        BranchNames.Require(branch);
        CommitMessage.RequireValidMessage(message, nameof(message));

        ArgumentException.ThrowIfNullOrEmpty(captureId);
        if (captureId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new ArgumentException("A capture id cannot contain whitespace or control characters.", nameof(captureId));
        }

        Branch = branch;
        Message = message.Trim();
        CaptureId = captureId;
    }

    /// <summary>The branch the point is recorded on.</summary>
    public string Branch { get; }

    /// <summary>The commit subject, without leading or trailing whitespace.</summary>
    public string Message { get; }

    /// <summary>The value of the <c>Savepoint-Capture</c> trailer.</summary>
    public string CaptureId { get; }

    /// <summary>How the capture relates to existing history; <see cref="CaptureMode.Normal"/> by default.</summary>
    public CaptureMode Mode { get; init; } = CaptureMode.Normal;

    /// <summary>
    /// When the capture event began, written into every commit it creates as the <c>Savepoint-Captured-At</c> trailer (to the
    /// millisecond) so points are ordered exactly even when two fall in one second. The engine's clock when null.
    /// </summary>
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>The game's ignore patterns, in gitignore syntax, one per entry. They replace the previous list on every capture.</summary>
    public IReadOnlyList<string> IgnorePatterns { get; init; } = [];

    /// <summary>
    /// The commit the playing session started from. <see cref="CaptureMode.MoveForward"/> requires it, and both it
    /// and <see cref="CaptureMode.Fork"/> refuse unless the folder is on this commit or a descendant of it.
    /// </summary>
    public string? ExpectedStartCommit { get; init; }
}
