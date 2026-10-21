namespace Savepoint.Core.Api;

/// <summary>What a launch should load. The cases are closed: the service handles exactly these three.</summary>
public abstract record LaunchTarget
{
    private LaunchTarget()
    {
    }

    /// <summary>The profile's latest install and latest save: a plain Play.</summary>
    /// <param name="ProfileId">The profile to launch.</param>
    public sealed record ProfileTip(string ProfileId) : LaunchTarget;

    /// <summary>The profile's latest install with a save chosen in the launch dialog's picker.</summary>
    /// <param name="ProfileId">The profile to launch.</param>
    /// <param name="SaveId">The chosen save, one of the preview's <see cref="LaunchPreview.AvailableSaves"/>.</param>
    public sealed record Save(string ProfileId, HistoryPointId SaveId) : LaunchTarget;

    /// <summary>Both folders as they were at one history point, for "Go back to this" (D15). The profile is the point's own.</summary>
    /// <param name="PointId">The history point to restore.</param>
    public sealed record Point(HistoryPointId PointId) : LaunchTarget;
}
