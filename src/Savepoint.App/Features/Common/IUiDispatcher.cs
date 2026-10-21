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

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread once everything already waiting there, including the next frame, has had its
    /// turn. Work that must not delay a screen uses it. The default runs it like <see cref="Post"/>.
    /// </summary>
    void PostWhenIdle(Action action) => Post(action);
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

    /// <inheritdoc />
    public void PostWhenIdle(Action action) => Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
}

/// <summary>Runs every action on the calling thread at once; for tests, where the fake backend already raises events on the test's thread.</summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => action();
}
