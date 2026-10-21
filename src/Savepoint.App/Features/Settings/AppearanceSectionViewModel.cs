using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Settings;

/// <summary>
/// The Appearance section (mockups 2k and 3c): theme, layout and accent. A theme or accent choice is saved and the
/// shell follows at once; a layout choice goes through the shell, which saves it, and the selection follows the shell
/// when the layout changes on the Games screen. A choice that fails to save goes back to what is stored.
/// </summary>
public sealed partial class AppearanceSectionViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    private readonly IShellContext _shell;
    private ThemeOption _currentTheme;
    private LayoutChoice _currentLayout;
    private AccentOption _currentAccent;
    private bool _restoring;

    /// <summary>The theme segment that is selected; choosing another saves it.</summary>
    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    /// <summary>The layout preview that is selected; choosing the other switches the shell's layout.</summary>
    [ObservableProperty]
    private LayoutChoice? _selectedLayout;

    /// <summary>The accent swatch that is selected; choosing another saves it.</summary>
    [ObservableProperty]
    private AccentOption? _selectedAccent;

    internal AppearanceSectionViewModel(SettingsStore store, IShellContext shell, UiFormatter text)
    {
        _store = store;
        _shell = shell;
        ThemeOptions =
        [
            new ThemeOption(ThemePreference.System, text.Message("settings.theme.system")),
            new ThemeOption(ThemePreference.Light, text.Message("settings.theme.light")),
            new ThemeOption(ThemePreference.Dark, text.Message("settings.theme.dark")),
        ];
        LayoutChoices =
        [
            new LayoutChoice(ShellLayout.Sidebar, text.Message("settings.layout.sidebar"), text.Message("settings.layout.sidebarHint")),
            new LayoutChoice(ShellLayout.Library, text.Message("settings.layout.library"), text.Message("settings.layout.libraryHint")),
        ];
        AccentOptions =
        [
            new AccentOption(AccentColorId.Blue, text.Message("settings.accent.blue")),
            new AccentOption(AccentColorId.Violet, text.Message("settings.accent.violet")),
            new AccentOption(AccentColorId.Green, text.Message("settings.accent.green")),
            new AccentOption(AccentColorId.Amber, text.Message("settings.accent.amber")),
            new AccentOption(AccentColorId.Crimson, text.Message("settings.accent.crimson")),
        ];

        var defaults = AppSettings.Defaults(string.Empty);
        _currentTheme = ThemeFor(defaults.Theme);
        _currentLayout = LayoutFor(defaults.Layout);
        _currentAccent = AccentFor(defaults.AccentColorId);
        _selectedTheme = _currentTheme;
        _selectedLayout = _currentLayout;
        _selectedAccent = _currentAccent;

        store.Changed += (_, settings) => Show(settings);
        shell.PropertyChanged += OnShellPropertyChanged;
    }

    /// <summary>Match the system, Light and Dark, in that order; the first names the operating system.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    /// <summary>The Sidebar and Library previews, in that order.</summary>
    public IReadOnlyList<LayoutChoice> LayoutChoices { get; }

    /// <summary>The five accent presets, in the order the spec draws them.</summary>
    public IReadOnlyList<AccentOption> AccentOptions { get; }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (_restoring)
        {
            return;
        }

        // A list box clears its selection while it rebuilds; only a real choice counts.
        if (value is null)
        {
            Restore(() => SelectedTheme = _currentTheme);
            return;
        }

        if (value != _currentTheme)
        {
            _currentTheme = value;
            _ = SaveAsync(settings => settings with { Theme = value.Preference });
        }
    }

    partial void OnSelectedAccentChanged(AccentOption? value)
    {
        if (_restoring)
        {
            return;
        }

        if (value is null)
        {
            Restore(() => SelectedAccent = _currentAccent);
            return;
        }

        if (value != _currentAccent)
        {
            _currentAccent = value;
            _ = SaveAsync(settings => settings with { AccentColorId = value.Accent });
        }
    }

    partial void OnSelectedLayoutChanged(LayoutChoice? value)
    {
        if (_restoring)
        {
            return;
        }

        if (value is null)
        {
            Restore(() => SelectedLayout = _currentLayout);
            return;
        }

        if (value != _currentLayout)
        {
            _currentLayout = value;
            _ = _shell.SetLayoutAsync(value.Layout);
        }
    }

    private async Task SaveAsync(Func<AppSettings, AppSettings> change)
    {
        // A failed save leaves the stored values as they were, and the controls go back to them.
        if (!await _store.UpdateAsync(change) && _store.Current is { } stored)
        {
            Show(stored);
        }
    }

    private void Show(AppSettings settings) =>
        Restore(() =>
        {
            _currentTheme = ThemeFor(settings.Theme);
            _currentAccent = AccentFor(settings.AccentColorId);
            _currentLayout = LayoutFor(settings.Layout);
            SelectedTheme = _currentTheme;
            SelectedAccent = _currentAccent;
            SelectedLayout = _currentLayout;
        });

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IShellContext.Layout))
        {
            Restore(() =>
            {
                _currentLayout = LayoutFor(_shell.Layout);
                SelectedLayout = _currentLayout;
            });
        }
    }

    private ThemeOption ThemeFor(ThemePreference preference) => ThemeOptions.First(option => option.Preference == preference);

    private LayoutChoice LayoutFor(ShellLayout layout) => LayoutChoices.First(choice => choice.Layout == layout);

    private AccentOption AccentFor(AccentColorId accent) => AccentOptions.First(option => option.Accent == accent);

    // Sets the selections the screen itself owns without that counting as a choice by the user.
    private void Restore(Action change)
    {
        _restoring = true;
        try
        {
            change();
        }
        finally
        {
            _restoring = false;
        }
    }
}
