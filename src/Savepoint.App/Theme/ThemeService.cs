using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Theme;

// Turns a theme preference and accent into the Md3* color and brush resources; views follow with DynamicResource.
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

    public ThemeService(IResourceDictionary resources, ISystemThemeSource systemTheme, Action<ThemeVariant>? setRequestedVariant = null)
    {
        _resources = resources;
        _systemTheme = systemTheme;
        _setRequestedVariant = setRequestedVariant;
        _motion = new MotionResources(resources);
        _systemTheme.Changed += OnSystemThemeChanged;
    }

    public event EventHandler? Changed;

    public ThemePreference Preference { get; private set; } = ThemePreference.System;

    public AccentColorId Accent { get; private set; } = AccentColorId.Blue;

    // The mode actually in effect, after resolving System against the operating system.
    public bool IsDark { get; private set; }

    // Zeroes every transition duration; the setting arrives from outside (OS preference, user setting).
    public bool ReduceMotion
    {
        get => _reduceMotion;
        set
        {
            _reduceMotion = value;
            _motion.Apply(value);
        }
    }

    public static ThemeService CreateForApplication(Application application) =>
        new(application.Resources, new PlatformSystemThemeSource(application.PlatformSettings), variant => application.RequestedThemeVariant = variant);

    public void Apply(ThemePreference preference, AccentColorId accent)
    {
        Preference = preference;
        Accent = accent;
        Refresh();
    }

    public void SetPreference(ThemePreference preference) => Apply(preference, Accent);

    public void SetAccent(AccentColorId accent) => Apply(Preference, accent);

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
