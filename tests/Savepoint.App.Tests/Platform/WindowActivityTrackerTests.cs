using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Platform;

namespace Savepoint.App.Tests.Platform;

public class WindowActivityTrackerTests
{
    private static Window ShownWindow()
    {
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        return window;
    }

    // The headless platform never activates a window by itself, so this calls the callbacks a real platform would.
    private static void PlatformActivates(Window window) => InvokePlatformCallback(window, "Activated");

    private static void PlatformDeactivates(Window window) => InvokePlatformCallback(window, "Deactivated");

    private static void InvokePlatformCallback(Window window, string name)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("The window has no platform implementation.");
        var property = impl.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var callback = (Action?)property?.GetValue(impl) ?? throw new InvalidOperationException($"{impl.GetType().Name} has no {name} callback.");
        callback();
    }

    [AvaloniaFact]
    public void A_window_nobody_activated_is_neither_active_nor_minimized()
    {
        var window = ShownWindow();

        using var tracker = new WindowActivityTracker(window);

        Assert.Equal(new WindowActivity(false, false), tracker.Current);
    }

    [AvaloniaFact]
    public void An_already_active_window_is_read_at_construction()
    {
        var window = ShownWindow();
        PlatformActivates(window);

        using var tracker = new WindowActivityTracker(window);

        Assert.True(tracker.Current.IsActive);
    }

    [AvaloniaFact]
    public void An_already_minimized_window_is_read_at_construction()
    {
        var window = ShownWindow();
        window.WindowState = WindowState.Minimized;

        using var tracker = new WindowActivityTracker(window);

        Assert.True(tracker.Current.IsMinimized);
    }

    [AvaloniaFact]
    public void Activation_makes_the_window_active_and_announces_it_once()
    {
        var window = ShownWindow();
        using var tracker = new WindowActivityTracker(window);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        PlatformActivates(window);
        PlatformActivates(window);

        Assert.True(tracker.Current.IsActive);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Deactivation_makes_the_window_inactive_and_announces_it()
    {
        var window = ShownWindow();
        PlatformActivates(window);
        using var tracker = new WindowActivityTracker(window);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        PlatformDeactivates(window);

        Assert.False(tracker.Current.IsActive);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Minimizing_and_restoring_follow_the_window_state()
    {
        var window = ShownWindow();
        using var tracker = new WindowActivityTracker(window);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        window.WindowState = WindowState.Minimized;
        var whileMinimized = tracker.Current;
        window.WindowState = WindowState.Normal;
        var afterRestore = tracker.Current;

        Assert.True(whileMinimized.IsMinimized);
        Assert.False(afterRestore.IsMinimized);
        Assert.Equal(2, changes);
    }

    [AvaloniaFact]
    public void Maximizing_does_not_count_as_a_change()
    {
        var window = ShownWindow();
        using var tracker = new WindowActivityTracker(window);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;

        window.WindowState = WindowState.Maximized;

        Assert.False(tracker.Current.IsMinimized);
        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void A_minimized_window_that_is_still_active_reports_both()
    {
        var window = ShownWindow();
        PlatformActivates(window);
        using var tracker = new WindowActivityTracker(window);

        window.WindowState = WindowState.Minimized;

        Assert.Equal(new WindowActivity(IsActive: true, IsMinimized: true), tracker.Current);
    }

    [AvaloniaFact]
    public void Closing_the_window_makes_it_inactive()
    {
        var window = ShownWindow();
        PlatformActivates(window);
        using var tracker = new WindowActivityTracker(window);

        window.Close();

        Assert.False(tracker.Current.IsActive);
    }

    [AvaloniaFact]
    public void A_disposed_tracker_stops_following_the_window()
    {
        var window = ShownWindow();
        var tracker = new WindowActivityTracker(window);
        var changes = 0;
        tracker.Changed += (_, _) => changes++;
        tracker.Dispose();

        PlatformActivates(window);
        window.WindowState = WindowState.Minimized;

        Assert.Equal(0, changes);
        Assert.Equal(new WindowActivity(false, false), tracker.Current);
    }

    [AvaloniaFact]
    public async Task The_state_can_be_read_from_a_background_thread()
    {
        var window = ShownWindow();
        PlatformActivates(window);
        using var tracker = new WindowActivityTracker(window);

        var seen = await Task.Run(() => tracker.Current, TestContext.Current.CancellationToken);

        Assert.True(seen.IsActive);
    }
}
