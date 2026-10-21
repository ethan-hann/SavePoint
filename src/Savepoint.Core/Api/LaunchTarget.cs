namespace Savepoint.Core.Api;

public abstract record LaunchTarget
{
    // Closed: the service handles exactly these three.
    private LaunchTarget()
    {
    }

    // The profile's latest install and latest save.
    public sealed record ProfileTip(string ProfileId) : LaunchTarget;

    // The profile's latest install with a chosen save from the launch dialog's picker.
    public sealed record Save(string ProfileId, HistoryPointId SaveId) : LaunchTarget;

    // Both folders as they were at that point, for "Go back to this" (D15).
    public sealed record Point(HistoryPointId PointId) : LaunchTarget;
}
