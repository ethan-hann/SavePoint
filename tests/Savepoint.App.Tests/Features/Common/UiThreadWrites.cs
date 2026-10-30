using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;

namespace Savepoint.App.Tests.Features.Common;

// Records every change a view model or list announces from a thread other than the UI thread. A bound control throws on such a change only
// while it is on screen, and a handler that wrote there and one that posted look the same to a test that waits for the end state.
internal sealed class UiThreadWrites
{
    private readonly List<string> _offenders = [];
    private readonly object _gate = new();

    public UiThreadWrites Watch(INotifyPropertyChanged source)
    {
        source.PropertyChanged += (_, e) => Record($"{source.GetType().Name}.{e.PropertyName}");
        return this;
    }

    public UiThreadWrites WatchList(INotifyCollectionChanged source)
    {
        source.CollectionChanged += (_, e) => Record($"{source.GetType().Name} {e.Action}");
        return this;
    }

    public IReadOnlyList<string> Offenders
    {
        get
        {
            lock (_gate)
            {
                return [.. _offenders];
            }
        }
    }

    private void Record(string what)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        lock (_gate)
        {
            _offenders.Add(what);
        }
    }
}
