namespace Savepoint.Core.Api;

// PointName is what the new point shows in history (D14 calls it the message).
public abstract record SessionCompletion
{
    // Closed: the cases mirror CompletionChoice.
    private SessionCompletion()
    {
    }

    public sealed record CaptureNew(string PointName) : SessionCompletion;

    // Replaces the latest save point; the UI confirms first because it cannot be undone.
    public sealed record UpdateExisting(string PointName) : SessionCompletion;

    public sealed record ForkNewProfile(string ProfileName, string PointName) : SessionCompletion;

    // Moves the profile on from the older point; the newer points leave it (D14).
    public sealed record MoveForward(string PointName) : SessionCompletion;

    public sealed record Skip() : SessionCompletion;
}
