namespace Savepoint.Core.Api;

/// <summary>The choices the post-play dialog can offer. Each matches one <see cref="SessionCompletion"/> case.</summary>
public enum CompletionChoice
{
    /// <summary>Save the session as a new point (<see cref="SessionCompletion.CaptureNew"/>).</summary>
    CaptureNew,

    /// <summary>Replace the latest save point with the session (<see cref="SessionCompletion.UpdateExisting"/>).</summary>
    UpdateExisting,

    /// <summary>
    /// Keep the session as a new profile (<see cref="SessionCompletion.ForkNewProfile"/>). Offered after playing from an older point.
    /// </summary>
    ForkNewProfile,

    /// <summary>Continue the profile from the older point, dropping the newer ones (<see cref="SessionCompletion.MoveForward"/>).</summary>
    MoveForward,

    /// <summary>Save nothing (<see cref="SessionCompletion.Skip"/>).</summary>
    Skip,
}
