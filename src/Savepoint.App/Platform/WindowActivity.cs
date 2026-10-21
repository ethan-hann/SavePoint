namespace Savepoint.App.Platform;

/// <summary>What the user can currently see of the main window.</summary>
/// <param name="IsActive">True while the window has the focus of the operating system.</param>
/// <param name="IsMinimized">True while the window is minimized; some platforms keep reporting a minimized window as active.</param>
public readonly record struct WindowActivity(bool IsActive, bool IsMinimized);

/// <summary>Reports whether the main window is in front of the user, from any thread.</summary>
public interface IWindowActivitySource
{
    /// <summary>The latest state. Safe to read from any thread, which the job events need.</summary>
    WindowActivity Current { get; }

    /// <summary>Raised on the UI thread after <see cref="Current"/> changed.</summary>
    event EventHandler? Changed;
}
