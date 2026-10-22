using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace Savepoint.App.Theme;

/// <summary>
/// Keeps the <c>reduced-motion</c> class on a window in step with <see cref="ThemeService.ReduceMotion"/>, so a style can
/// stop an animation and show something still. The base styles switch it on for every window.
/// </summary>
public static class WindowMotion
{
    /// <summary>The class a window carries while motion is reduced.</summary>
    public const string ClassName = "reduced-motion";

    /// <summary>True makes the window follow the application's theme service; the base styles set it on every window.</summary>
    public static readonly AttachedProperty<bool> FollowsProperty =
        AvaloniaProperty.RegisterAttached<Window, Window, bool>("Follows");

    private static readonly ConditionalWeakTable<Window, Follower> _followers = [];

    static WindowMotion() => FollowsProperty.Changed.AddClassHandler<Window>(OnFollowsChanged);

    /// <summary>Reads <see cref="FollowsProperty"/>.</summary>
    public static bool GetFollows(Window window) => window.GetValue(FollowsProperty);

    /// <summary>Writes <see cref="FollowsProperty"/>.</summary>
    public static void SetFollows(Window window, bool value) => window.SetValue(FollowsProperty, value);

    private static void OnFollowsChanged(Window window, AvaloniaPropertyChangedEventArgs e)
    {
        if (_followers.TryGetValue(window, out var existing))
        {
            existing.Dispose();
            _followers.Remove(window);
        }

        if (e.NewValue is true && (Application.Current as App)?.Theme is { } theme)
        {
            _followers.Add(window, new Follower(window, theme));
        }
    }

    private sealed class Follower : IDisposable
    {
        private readonly Window _window;
        private readonly ThemeService _theme;

        public Follower(Window window, ThemeService theme)
        {
            _window = window;
            _theme = theme;
            Sync();
            _theme.ReduceMotionChanged += OnChanged;
            _window.Closed += OnClosed;
        }

        public void Dispose()
        {
            _theme.ReduceMotionChanged -= OnChanged;
            _window.Closed -= OnClosed;
        }

        private void OnChanged(object? sender, EventArgs e) => Sync();

        private void OnClosed(object? sender, EventArgs e) => Dispose();

        private void Sync() => _window.Classes.Set(ClassName, _theme.ReduceMotion);
    }
}
