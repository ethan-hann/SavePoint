namespace Savepoint.App.Features.Common.Accessibility;

/// <summary>
/// Decides which progress milestones are worth speaking: each quarter (25, 50 and 75 percent) once, and only the
/// highest one crossed when a report jumps over several, so a fast job does not queue a string of announcements.
/// </summary>
public sealed class ProgressMilestones
{
    private static readonly int[] _quarters = [25, 50, 75];

    private int _last;

    /// <summary>The quarter this report newly reached, or null when it reached none; progress may only move forward.</summary>
    /// <param name="percent">How far along the job is, from 0 to 100.</param>
    public int? Crossed(int percent)
    {
        var crossed = _quarters.Where(quarter => quarter <= percent && quarter > _last).DefaultIfEmpty(0).Max();
        if (crossed == 0)
        {
            return null;
        }

        _last = crossed;
        return crossed;
    }

    /// <summary>Forgets what was announced, for a job that starts over.</summary>
    public void Reset() => _last = 0;
}
