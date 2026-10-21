using Avalonia.Threading;

namespace Savepoint.App.Features.Common;

/// <summary>
/// Runs work on the UI thread. Api events and timers fire on background threads, so view models hand their handlers
/// to this instead of touching bound state directly; tests pass <see cref="ImmediateUiDispatcher"/>.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Runs <paramref name="action"/> on the UI thread: at once when already there, otherwise queued.</summary>
    void Post(Action action);
}

/// <summary>The real dispatcher, backed by Avalonia's UI thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}

/// <summary>Runs every action on the calling thread at once; for tests, where the fake backend already raises events on the test's thread.</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => action();
}
