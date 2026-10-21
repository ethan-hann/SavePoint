using Savepoint.App.Platform.Notifications;

namespace Savepoint.App.Tests.Platform.Fakes;

// Keeps the notification area in memory: the icons that exist, and every request in order.
internal sealed class FakeShellNotifyIcon : IShellNotifyIcon
{
    private readonly object _gate = new();
    private readonly List<NotifyIconRequest> _requests = [];
    private readonly HashSet<(nint Window, uint Id)> _icons = [];

    // Answers false for a request to play a shell that refuses it.
    public Func<NotifyIconRequest, bool> Accepts { get; set; } = _ => true;

    public Exception? Throws { get; set; }

    public IReadOnlyList<NotifyIconRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public int IconCount
    {
        get
        {
            lock (_gate)
            {
                return _icons.Count;
            }
        }
    }

    public bool Send(NotifyIconRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
            if (Throws is not null)
            {
                throw Throws;
            }

            if (!Accepts(request))
            {
                return false;
            }

            var key = (request.WindowHandle, request.Id);
            switch (request.Command)
            {
                case NotifyIconCommand.Add:
                    return _icons.Add(key);
                case NotifyIconCommand.Modify:
                    return _icons.Contains(key);
                default:
                    return _icons.Remove(key);
            }
        }
    }

    // Explorer restarting takes every icon with it.
    public void ExplorerRestarts()
    {
        lock (_gate)
        {
            _icons.Clear();
        }
    }
}
