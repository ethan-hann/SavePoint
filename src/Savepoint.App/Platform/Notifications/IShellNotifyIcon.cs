namespace Savepoint.App.Platform.Notifications;

/// <summary>The shell's notification area as far as notifications need it, behind a seam so tests never touch the real tray.</summary>
internal interface IShellNotifyIcon
{
    /// <summary>Hands the request to the shell.</summary>
    /// <returns>False when the shell refused it, for example because Explorer is not running or has restarted.</returns>
    bool Send(NotifyIconRequest request);
}
