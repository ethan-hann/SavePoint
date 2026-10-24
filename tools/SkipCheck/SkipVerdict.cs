namespace Savepoint.Tools.SkipCheck;

/// <summary>How the skips of one run compare with the checked-in list.</summary>
/// <param name="Unexpected">Tests that skipped though the list does not name them, in name order; any of these fails the gate.</param>
/// <param name="NowRunning">Tests the list names that ran this time, in name order; worth a note, never a failure.</param>
/// <param name="NotInReport">How many tests the list names that the report does not mention (a narrowed run, or a removed test).</param>
/// <param name="TotalTests">How many distinct tests the report holds.</param>
public sealed record SkipVerdict(IReadOnlyList<string> Unexpected, IReadOnlyList<string> NowRunning, int NotInReport, int TotalTests)
{
    /// <summary>True when every skip was expected.</summary>
    public bool Passed => Unexpected.Count == 0;

    /// <summary>Compares a run's skips with a list.</summary>
    public static SkipVerdict Compare(SkipList list, XunitReport report)
    {
        var unexpected = report.Skipped.Keys
            .Where(name => !list.Expected.Contains(name) && !list.Intermittent.Contains(name))
            .Order(StringComparer.Ordinal)
            .ToList();
        var nowRunning = list.Expected.Where(report.Ran.Contains).Order(StringComparer.Ordinal).ToList();
        var notInReport = list.Expected.Count(name => !report.Ran.Contains(name) && !report.Skipped.ContainsKey(name));
        return new SkipVerdict(unexpected, nowRunning, notInReport, report.Ran.Count + report.Skipped.Count);
    }
}
