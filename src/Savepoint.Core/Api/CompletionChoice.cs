namespace Savepoint.Core.Api;

// The post-play choices, matching the SessionCompletion cases one for one.
public enum CompletionChoice
{
    CaptureNew,
    UpdateExisting,
    ForkNewProfile,
    MoveForward,
    Skip,
}
