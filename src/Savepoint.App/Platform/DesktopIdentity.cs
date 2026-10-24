namespace Savepoint.App.Platform;

/// <summary>The names the Linux desktop uses to tell this app's windows from every other program's.</summary>
internal static class DesktopIdentity
{
    /// <summary>
    /// The window class the app's windows carry. The AppImage's desktop entry names the same value as its <c>StartupWMClass</c>, which
    /// is how the shell finds the entry (and so the icon and name) that belongs to a running window.
    /// </summary>
    public const string WindowClass = "SavePointApp";
}
