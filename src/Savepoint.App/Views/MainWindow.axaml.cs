using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;
using Savepoint.App.Theme;

namespace Savepoint.App.Views;

public partial class MainWindow : Window
{
    /// <summary>A screen's working area in device pixels and its own display scaling.</summary>
    /// <param name="WorkArea">The screen less the taskbar or dock.</param>
    /// <param name="Scaling">The operating system's scaling of that screen.</param>
    internal readonly record struct ScreenRoom(PixelRect WorkArea, double Scaling);

    /// <summary>Tells which screen the window is on; the platform answers when null. A test sets it, since the headless platform has one fixed screen.</summary>
    internal Func<ScreenRoom?>? ScreenSource { get; set; }

    private bool _closeApproved;
    private bool _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        FollowUiScale();
    }

    private ThemeService? _theme;
    private Size _baseMinimum;
    private ScreenRoom? _fitted;
    private bool _fitting;

    private enum FitReason
    {
        Opened,
        ScaleChanged,
        ScreenMayHaveChanged,
    }

    // The smallest size set in the XAML holds at 100%; a larger scale makes the content larger, so the smallest size grows with it.
    // The screen decides which scales have room, so everything is worked out again when the window opens, moves to another screen,
    // or the screen's own scaling changes.
    private void FollowUiScale()
    {
        if (Application.Current is not App { Theme: { } theme })
        {
            return;
        }

        _theme = theme;
        _baseMinimum = new Size(MinWidth, MinHeight);
        void OnScaleChanged(object? sender, EventArgs e) => Fit(FitReason.ScaleChanged);
        void OnScreenMayHaveChanged(object? sender, EventArgs e) => Fit(FitReason.ScreenMayHaveChanged);
        theme.UiScaleChanged += OnScaleChanged;
        Screens.Changed += OnScreenMayHaveChanged;
        PositionChanged += (_, _) => Fit(FitReason.ScreenMayHaveChanged);
        ScalingChanged += OnScreenMayHaveChanged;
        Closed += (_, _) =>
        {
            theme.UiScaleChanged -= OnScaleChanged;
            Screens.Changed -= OnScreenMayHaveChanged;

            // With no window there is no screen to limit the sizes by.
            theme.SetUiScaleRoom(null);
        };
        Opened += (_, _) => Fit(FitReason.Opened);
        Fit(FitReason.ScaleChanged);
    }

    private ScreenRoom? CurrentScreen()
    {
        if (ScreenSource is { } source)
        {
            return source();
        }

        return Screens.ScreenFromWindow(this) is { } screen ? new ScreenRoom(screen.WorkingArea, screen.Scaling) : null;
    }

    // Sets the smallest size for the screen and scale, grows or shrinks the window to hold it, and keeps the window inside the working area.
    // Before the window opens its screen is not known, so the first pass skips the cap; the pass on opening runs it and centers the window.
    private void Fit(FitReason reason)
    {
        if (_theme is not { } theme || _fitting)
        {
            return;
        }

        var screen = CurrentScreen();
        if (reason == FitReason.ScreenMayHaveChanged && (screen is null || screen == _fitted))
        {
            return;
        }

        _fitting = true;
        try
        {
            _fitted = screen;
            theme.SetUiScaleRoom(screen is { } known ? UiScale.RoomOf(known.WorkArea, known.Scaling) : null);
            var minimum = ScaledMinimum(_baseMinimum, theme.UiScalePercent, screen?.WorkArea, screen?.Scaling ?? 1);
            MinWidth = minimum.Width;
            MinHeight = minimum.Height;

            var current = IsVisible ? ClientSize : new Size(Width, Height);
            var room = screen is { } s ? UiScale.RoomOf(s.WorkArea, s.Scaling) : (Size?)null;
            var target = new Size(
                Math.Max(minimum.Width, Math.Min(current.Width, room?.Width ?? double.PositiveInfinity)),
                Math.Max(minimum.Height, Math.Min(current.Height, room?.Height ?? double.PositiveInfinity)));
            if (target != current && !double.IsNaN(target.Width) && !double.IsNaN(target.Height))
            {
                Width = target.Width;
                Height = target.Height;
            }

            if (screen is { } placed && IsVisible)
            {
                Place(placed, target, center: reason == FitReason.Opened);
            }
        }
        finally
        {
            _fitting = false;
        }
    }

    // Moves the window so all of it, frame included, is inside the working area; on opening it is centered there instead.
    private void Place(ScreenRoom screen, Size client, bool center)
    {
        var frame = FrameSize is { } outer ? outer - ClientSize : default;
        var width = (int)Math.Ceiling((client.Width + frame.Width) * screen.Scaling);
        var height = (int)Math.Ceiling((client.Height + frame.Height) * screen.Scaling);
        var area = screen.WorkArea;
        var x = center ? area.X + ((area.Width - width) / 2) : Position.X;
        var y = center ? area.Y + ((area.Height - height) / 2) : Position.Y;

        // The left and top edges win when the window is larger than the area, so its title bar stays reachable.
        x = Math.Max(area.X, Math.Min(x, area.Right - width));
        y = Math.Max(area.Y, Math.Min(y, area.Bottom - height));
        if (new PixelPoint(x, y) != Position)
        {
            Position = new PixelPoint(x, y);
        }
    }

    /// <summary>
    /// The window's smallest size at a UI scale: the size at 100% times the scale, but never more than the room the screen leaves,
    /// since a window that does not fit its screen cannot be used at all.
    /// </summary>
    /// <param name="baseSize">The smallest size at 100%.</param>
    /// <param name="percent">The UI scale in percent.</param>
    /// <param name="workArea">The screen's working area in device pixels, or null when the window's screen is not known.</param>
    /// <param name="screenScaling">The screen's own display scaling, to turn the working area into the window's units.</param>
    internal static Size ScaledMinimum(Size baseSize, int percent, PixelRect? workArea, double screenScaling)
    {
        var scaled = new Size(baseSize.Width * percent / 100.0, baseSize.Height * percent / 100.0);
        if (workArea is not { } area || screenScaling <= 0)
        {
            return scaled;
        }

        var room = UiScale.RoomOf(area, screenScaling);
        return new Size(Math.Max(baseSize.Width, Math.Min(scaled.Width, room.Width)), Math.Max(baseSize.Height, Math.Min(scaled.Height, room.Height)));
    }

    private MainWindowViewModel? _followed;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_followed is not null)
        {
            _followed.MinimizeRequested -= OnMinimizeRequested;
            _followed.QuestionOpening -= OnQuestionOpening;
        }

        _followed = DataContext as MainWindowViewModel;
        if (_followed is not null)
        {
            _followed.MinimizeRequested += OnMinimizeRequested;
            _followed.QuestionOpening += OnQuestionOpening;
        }
    }

    // A question on a minimized window would go unseen.
    private void OnQuestionOpening(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
    }

    private void OnMinimizeRequested(object? sender, EventArgs e) => WindowState = WindowState.Minimized;

    // The window stays open while the exit guard asks; it closes itself again once the guard says it may. A quit from the application
    // (Cmd+Q on macOS) arrives as ApplicationShutdown and honors the cancel; only the system ending the session cannot be held up.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved || DataContext is not MainWindowViewModel main || e.CloseReason is WindowCloseReason.OSShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (!_closeRequested)
        {
            _closeRequested = true;
            _ = CloseWhenAllowedAsync(main);
        }
    }

    private async Task CloseWhenAllowedAsync(MainWindowViewModel main)
    {
        try
        {
            _closeApproved = await main.PrepareToCloseAsync();
        }
        finally
        {
            _closeRequested = false;
        }

        // Posted, so the close never runs inside the closing event that asked for it (that happens whenever nothing is running).
        if (_closeApproved)
        {
            Dispatcher.UIThread.Post(Close);
        }
    }
}
