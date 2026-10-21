namespace Savepoint.Core.Errors;

/// <summary>The launch side of the app is not part of this build, so no game can be started or tracked yet.</summary>
public sealed class LaunchUnavailableException : SavepointException
{
    /// <summary>Creates the error.</summary>
    public LaunchUnavailableException()
        : base("error.launch.unavailable", "Launching games is not available in this build.")
    {
    }
}
