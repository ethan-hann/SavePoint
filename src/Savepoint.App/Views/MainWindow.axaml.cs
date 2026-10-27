using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Savepoint.App.Features.Shell;
using Savepoint.App.Platform;

namespace Savepoint.App.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    private bool _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Load();
        FollowUiScale();
    }

    // The smallest size set in the XAML holds at 100%; a larger scale makes the content larger, so the smallest size grows with it.
    private void FollowUiScale()
    {
        if (Application.Current is not App { Theme: { } theme })
        {
            return;
        }

        var baseSize = new Size(MinWidth, MinHeight);
        void Apply()
        {
            var screen = Screens.ScreenFromWindow(this);
            var size = ScaledMinimum(baseSize, theme.UiScalePercent, screen?.WorkingArea, screen?.Scaling ?? 1);
            MinWidth = size.Width;
            MinHeight = size.Height;
        }

        void OnScaleChanged(object? sender, EventArgs e) => Apply();
        theme.UiScaleChanged += OnScaleChanged;
        Closed += (_, _) => theme.UiScaleChanged -= OnScaleChanged;
        Opened += (_, _) => Apply();
        Apply();
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

        // The title bar and frame are not part of the client size the minimum applies to.
        const double frame = 40;
        return new Size(
            Math.Max(baseSize.Width, Math.Min(scaled.Width, (area.Width / screenScaling) - frame)),
            Math.Max(baseSize.Height, Math.Min(scaled.Height, (area.Height / screenScaling) - frame)));
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
