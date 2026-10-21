namespace Savepoint.Core.Git;

// Which git directories this engine is working on right now. A lock file in a directory nobody here is working on is stale.
internal sealed class InFlightTable
{
    private static readonly StringComparer _paths =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new(_paths);

    public Scope Enter(string gitDir)
    {
        lock (_gate)
        {
            _counts[gitDir] = _counts.GetValueOrDefault(gitDir) + 1;
        }

        return new Scope(this, gitDir);
    }

    public bool IsBusy(string gitDir)
    {
        lock (_gate)
        {
            return _counts.GetValueOrDefault(gitDir) > 0;
        }
    }

    private void Leave(string gitDir)
    {
        lock (_gate)
        {
            if (_counts.TryGetValue(gitDir, out var count) && count > 1)
            {
                _counts[gitDir] = count - 1;
            }
            else
            {
                _counts.Remove(gitDir);
            }
        }
    }

    private int CountOf(string gitDir)
    {
        lock (_gate)
        {
            return _counts.GetValueOrDefault(gitDir);
        }
    }

    internal sealed class Scope : IDisposable
    {
        private readonly InFlightTable _table;
        private readonly string _gitDir;
        private bool _left;

        public Scope(InFlightTable table, string gitDir)
        {
            _table = table;
            _gitDir = gitDir;
        }

        // True when this scope is the only operation on the directory, so a lock file there cannot belong to anyone else here.
        public bool IsAlone => _table.CountOf(_gitDir) == 1;

        public void Dispose()
        {
            if (!_left)
            {
                _left = true;
                _table.Leave(_gitDir);
            }
        }
    }
}
