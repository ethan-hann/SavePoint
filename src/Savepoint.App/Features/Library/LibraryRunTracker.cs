namespace Savepoint.App.Features.Library;

/// <summary>
/// Tells the rest of the app that games are being added from a library, so closing the window can ask first (D31): a game cut off
/// between its folders and its catalog row would leave a file behind and lose the result list.
/// </summary>
public sealed class LibraryRunTracker
{
    private readonly object _gate = new();
    private readonly List<Action> _stops = [];

    /// <summary>True while a run is adding games.</summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _stops.Count > 0;
            }
        }
    }

    /// <summary>Marks a run as started.</summary>
    /// <param name="requestStop">Asks the run to stop after the game it is on.</param>
    /// <returns>Dispose it when the run ends.</returns>
    public IDisposable Begin(Action requestStop)
    {
        ArgumentNullException.ThrowIfNull(requestStop);
        lock (_gate)
        {
            _stops.Add(requestStop);
        }

        return new Run(this, requestStop);
    }

    /// <summary>Asks every run to stop after the game it is on.</summary>
    public void RequestStop()
    {
        Action[] stops;
        lock (_gate)
        {
            stops = [.. _stops];
        }

        foreach (var stop in stops)
        {
            stop();
        }
    }

    private sealed class Run(LibraryRunTracker owner, Action stop) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (!_ended)
                {
                    _ended = true;
                    owner._stops.Remove(stop);
                }
            }
        }
    }
}
