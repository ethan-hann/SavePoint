using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Theme;

/// <summary>
/// Turns a theme preference and an accent into the application's color resources: a <c>Color</c> and a
/// <c>SolidColorBrush</c> for every role (see <see cref="ThemeKeys"/>), plus the Fluent keys in
/// <see cref="FluentBridge"/> and the motion resources. Views bind with <c>DynamicResource</c>, so switching repaints
/// them without rebuilding anything. Like the controls it themes it belongs to the UI thread; create it and call it there.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private readonly IResourceDictionary _resources;
    private readonly ISystemThemeSource _systemTheme;
    private readonly Action<ThemeVariant>? _setRequestedVariant;
    private readonly MotionResources _motion;
    // One brush per role for the life of the service, so a switch repaints bound controls without replacing anything.
    private readonly Dictionary<string, SolidColorBrush> _brushes = [];
    private bool _applied;
    private bool _reduceMotion;

    /// <summary>Publishes the motion resources straight away; the colors appear on the first <see cref="Apply"/>.</summary>
    /// <param name="resources">Receives every resource. The application's own resources in the app; a private dictionary in tests.</param>
    /// <param name="systemTheme">Consulted while the preference is <see cref="ThemePreference.System"/>; a change re-applies the theme.</param>
    /// <param name="setRequestedVariant">
    /// Called with <see cref="ThemeVariant.Light"/> or <see cref="ThemeVariant.Dark"/> (never Default) each time the mode
    /// is applied, so Fluent's own controls follow this service instead of a second subscription to the system.
    /// </param>
    public ThemeService(IResourceDictionary resources, ISystemThemeSource systemTheme, Action<ThemeVariant>? setRequestedVariant = null)
    {
        _resources = resources;
        _systemTheme = systemTheme;
        _setRequestedVariant = setRequestedVariant;
        _motion = new MotionResources(resources);
        _systemTheme.Changed += OnSystemThemeChanged;
    }

    /// <summary>Raised once per <see cref="Apply"/>, and once per system change while following the system, after every resource holds its new value.</summary>
    public event EventHandler? Changed;

    /// <summary>The preference last applied; <see cref="ThemePreference.System"/> until the first <see cref="Apply"/>.</summary>
    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    /// <summary>The accent last applied; <see cref="AccentColorId.Blue"/> until the first <see cref="Apply"/>.</summary>
    public AccentColorId Accent { get; private set; } = AccentColorId.Blue;

    /// <summary>The mode in effect, after resolving <see cref="ThemePreference.System"/> against the operating system.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised when <see cref="ReduceMotion"/> changes, after the motion resources hold their new values.</summary>
    public event EventHandler? ReduceMotionChanged;

    /// <summary>
    /// Zeroes every transition duration and the <c>Md3MotionDuration*</c> tokens while true, and puts the
    /// <c>reduced-motion</c> class on every window so styles can swap an animation for a still picture. The value is set
    /// from outside (the operating system's setting, a user setting), takes effect immediately and survives later
    /// <see cref="Apply"/> calls.
    /// </summary>
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            var changed = _reduceMotion != value;
            _reduceMotion = value;
            _motion.Apply(value);
            if (changed)
            {
                ReduceMotionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The service the app uses: the application's resources, the platform's light/dark setting, and Fluent's variant.</summary>
    public static ThemeService CreateForApplication(Application application) =>
        new(application.Resources, new PlatformSystemThemeSource(application.PlatformSettings), variant => application.RequestedThemeVariant = variant);

    /// <summary>
    /// Resolves the mode, publishes every role, sets Fluent's variant and raises <see cref="Changed"/>. Existing brushes
    /// are updated in place, so controls already bound to them repaint without being replaced.
    /// </summary>
    public void Apply(ThemePreference preference, AccentColorId accent)
    {
        Preference = preference;
        Accent = accent;
        Refresh();
    }

    /// <summary>Applies a new preference and keeps the current accent.</summary>
    public void SetPreference(ThemePreference preference) => Apply(preference, Accent);

    /// <summary>Applies a new accent and keeps the current preference.</summary>
    public void SetAccent(AccentColorId accent) => Apply(Preference, accent);

    /// <summary>Stops following the system and releases the system theme source if it holds anything.</summary>
    public void Dispose()
    {
        _systemTheme.Changed -= OnSystemThemeChanged;
        (_systemTheme as IDisposable)?.Dispose();
    }

    private void OnSystemThemeChanged(object? sender, EventArgs e)
    {
        if (_applied && Preference == ThemePreference.System)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var dark = Preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => _systemTheme.IsDark,
        };
        IsDark = dark;

        var roles = ThemeRoles.Build(AccentSeeds.For(Accent), dark);
        foreach (var (role, argb) in roles)
        {
            var color = Color.FromUInt32(argb);
            if (_brushes.TryGetValue(role, out var brush))
            {
                brush.Color = color;
            }
            else
            {
                brush = new SolidColorBrush(color);
                _brushes[role] = brush;
                _resources[ThemeKeys.Brush(role)] = brush;
            }

            _resources[ThemeKeys.Color(role)] = color;
        }

        foreach (var (fluentKey, role) in FluentBridge.Brushes)
        {
            _resources[fluentKey] = _brushes[role];
        }

        foreach (var (fluentKey, role) in FluentBridge.Colors)
        {
            _resources[fluentKey] = Color.FromUInt32(roles[role]);
        }

        // Always explicit, so Fluent's own controls follow this service rather than a second OS subscription.
        _setRequestedVariant?.Invoke(dark ? ThemeVariant.Dark : ThemeVariant.Light);
        _applied = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
