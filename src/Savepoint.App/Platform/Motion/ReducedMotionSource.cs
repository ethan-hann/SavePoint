using Savepoint.App.Platform.Processes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Platform.Motion;

/// <summary>
/// Follows the operating system's "reduce motion" preference. It starts with animations welcome, which is also the answer
/// whenever the system does not say. None of the three systems announces a change cheaply, so the preference is read again
/// when the main window is activated: <see cref="RefreshOnActivation"/>.
/// </summary>
public sealed class ReducedMotionSource
{
    private readonly object _gate = new();
    private readonly IReducedMotionReader _reader;
    private bool _isReduced;

    internal ReducedMotionSource(IReducedMotionReader reader)
    {
        _reader = reader;
    }

    /// <summary>Raised after <see cref="IsReduced"/> changed, on the synchronization context that called <see cref="RefreshAsync"/>, which is the UI thread when called from there and where <c>ThemeService.ReduceMotion</c> may be set.</summary>
    public event EventHandler? Changed;

    /// <summary>True when the user asks the system for less motion. False until a read says otherwise.</summary>
    public bool IsReduced
    {
        get
        {
            lock (_gate)
            {
                return _isReduced;
            }
        }
    }

    /// <summary>The source for the OS the app runs on: Windows settings, macOS <c>defaults</c> or GNOME <c>gsettings</c>.</summary>
    public static ReducedMotionSource CreateForCurrentOs() =>
        Create(PlatformFactory.CurrentOs, new SystemProcessRunner(), WindowsAnimationSetting.Read);

    /// <summary>Reads the preference off the calling thread and applies it. An unknown answer, or a failing read, changes nothing.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        bool? answer;
        try
        {
            // Starting a helper program takes a moment the UI thread should not spend.
            answer = await Task.Run(() => _reader.ReadAsync(cancellationToken).AsTask(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return;
        }

        if (answer is not { } reduced)
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = _isReduced != reduced;
            _isReduced = reduced;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reads the preference again each time the window becomes active.</summary>
    /// <param name="window">The window to watch.</param>
    /// <returns>Stops watching when disposed.</returns>
    public IDisposable RefreshOnActivation(IWindowActivitySource window)
    {
        EventHandler handler = (_, _) =>
        {
            if (window.Current.IsActive)
            {
                _ = RefreshQuietlyAsync();
            }
        };
        window.Changed += handler;
        return new Subscription(() => window.Changed -= handler);
    }

    internal static ReducedMotionSource Create(OsKind os, IProcessRunner runner, Func<bool?> windowsAnimationsEnabled) =>
        new(os switch
        {
            OsKind.Windows => new WindowsReducedMotionReader(windowsAnimationsEnabled),
            OsKind.MacOS => new MacReducedMotionReader(runner),
            _ => new GnomeReducedMotionReader(runner),
        });

    private async Task RefreshQuietlyAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception)
        {
            // An activation must never fail because the preference could not be read.
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _stop;

        public Subscription(Action stop)
        {
            _stop = stop;
        }

        public void Dispose() => _stop();
    }
}
