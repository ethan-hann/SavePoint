namespace Savepoint.App.Platform;

/// <summary>
/// Makes sure the player learns that a game session ended when the after-play dialog opens, even if another window
/// (Steam, a launcher) took the focus as the game quit. It never takes the focus itself.
/// </summary>
public interface ISessionAttention
{
    /// <summary>
    /// Called on the UI thread as the after-play dialog opens. Does nothing while the window is in front of the player;
    /// otherwise asks the operating system for attention and raises a notification. Never throws.
    /// </summary>
    /// <param name="gameName">The name of the game that ended, as the player knows it.</param>
    void SessionEnded(string gameName);
}

/// <summary>Asks the operating system to draw attention to the window without activating it.</summary>
public interface IWindowFlasher
{
    /// <summary>Starts the attention cue (a flashing taskbar button) if the platform has one. Never throws and never takes the focus.</summary>
    void Flash();
}

/// <summary>Stands in until the window exists, and wherever there is nothing to ask for attention with.</summary>
public sealed class NullSessionAttention : ISessionAttention
{
    /// <summary>The shared instance.</summary>
    public static readonly NullSessionAttention Instance = new();

    /// <inheritdoc />
    public void SessionEnded(string gameName)
    {
    }
}

/// <summary>
/// Forwards to the real <see cref="ISessionAttention"/> once the main window has one to give. The launch flow is built
/// before the window's host integration, so it holds this and the host fills in <see cref="Target"/> later.
/// </summary>
public sealed class SessionAttentionRelay : ISessionAttention
{
    private volatile ISessionAttention _target = NullSessionAttention.Instance;

    /// <summary>Where calls go; the do-nothing instance until set.</summary>
    public ISessionAttention Target
    {
        get => _target;
        set => _target = value;
    }

    /// <inheritdoc />
    public void SessionEnded(string gameName) => _target.SessionEnded(gameName);
}
