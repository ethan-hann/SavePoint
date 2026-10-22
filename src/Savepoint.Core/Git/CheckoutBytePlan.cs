namespace Savepoint.Core.Git;

// What git's checkout meter means in bytes. The meter counts the files git removes first, then the files it writes, each group in
// path order, so the bytes written after any count are known before git starts.
internal sealed class CheckoutBytePlan
{
    // Bytes written once this many files are done.
    private readonly long[] _after;

    public CheckoutBytePlan(IReadOnlyList<GitEngine.TreeChange> changes, IReadOnlyDictionary<string, long> sizes)
    {
        var removed = changes.Where(change => change.Status == 'D').OrderBy(change => change.Path, StringComparer.Ordinal);
        var written = changes.Where(change => change.Status != 'D').OrderBy(change => change.Path, StringComparer.Ordinal);
        _after = new long[changes.Count + 1];
        var index = 0;
        foreach (var change in removed.Concat(written))
        {
            _after[index + 1] = _after[index] + (change.Status == 'D' ? 0 : sizes.GetValueOrDefault(change.NewId));
            index++;
        }
    }

    // Files the plan expects git to handle.
    public int Entries => _after.Length - 1;

    // Bytes the switch writes in all.
    public long Total => _after[^1];

    // When git counts a different number of files than the plan expected (a half-finished earlier switch), the bytes are spread evenly over git's count.
    public long BytesAfter(int done, int gitTotal)
    {
        if (gitTotal <= 0)
        {
            return 0;
        }

        done = Math.Clamp(done, 0, gitTotal);
        return gitTotal == Entries ? _after[done] : (long)((decimal)Total * done / gitTotal);
    }
}
