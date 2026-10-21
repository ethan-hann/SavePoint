namespace Savepoint.Core.Api;

/// <summary>
/// How the user wants a finished play session saved. The cases are closed and mirror <see cref="CompletionChoice"/>. Every case but
/// <see cref="Skip"/> runs a capture job; the point name each carries is what the new point shows in history (D14 calls it the message).
/// </summary>
public abstract record SessionCompletion
{
    private SessionCompletion()
    {
    }

    /// <summary>Saves the session as a new point on the profile.</summary>
    /// <param name="PointName">The new point's name.</param>
    public sealed record CaptureNew(string PointName) : SessionCompletion;

    /// <summary>Replaces the profile's latest save point with the session. The UI confirms first because it cannot be undone.</summary>
    /// <param name="PointName">The name the replacing point shows in history.</param>
    public sealed record UpdateExisting(string PointName) : SessionCompletion;

    /// <summary>Keeps the session as a new profile that starts at the older point the player began from (D14).</summary>
    /// <param name="ProfileName">The new profile's name.</param>
    /// <param name="PointName">The first point's name.</param>
    public sealed record ForkNewProfile(string ProfileName, string PointName) : SessionCompletion;

    /// <summary>Moves the profile on from the older point the player began from; the newer points leave the profile (D14).</summary>
    /// <param name="PointName">The new point's name.</param>
    public sealed record MoveForward(string PointName) : SessionCompletion;

    /// <summary>Saves nothing and ends the session at once. It runs no job.</summary>
    public sealed record Skip() : SessionCompletion;
}
