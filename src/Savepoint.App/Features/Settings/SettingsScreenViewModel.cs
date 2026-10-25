using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Settings;

/// <summary>What the Settings screen is built from.</summary>
/// <param name="Settings">The settings Api, for reading.</param>
/// <param name="SettingsWriter">Saves every change; the same writer every other feature saves settings through.</param>
/// <param name="Jobs">Follows storage moves, optimization and clearing.</param>
/// <param name="Dialogs">Pickers and confirmations.</param>
/// <param name="Text">Formats the copy.</param>
/// <param name="Toasts">Reports a save that failed.</param>
/// <param name="Dispatcher">Brings Api events to the UI thread.</param>
/// <param name="Environment">The machine: the notices file, the file manager and the version.</param>
public sealed record SettingsDependencies(
    ISettingsApi Settings,
    SettingsWriter SettingsWriter,
    IJobsApi Jobs,
    IDialogs Dialogs,
    UiFormatter Text,
    ToastHostViewModel Toasts,
    IUiDispatcher Dispatcher,
    SettingsEnvironment Environment);

/// <summary>
/// The Settings screen: where SavePoint keeps its data, appearance, what happens when a game
/// stops, a way to remove all of SavePoint's data, and About. It reads the stored settings each time it is shown, so
/// a change made elsewhere (the layout toggle on the Games screen) is never overwritten by a stale copy.
/// </summary>
public sealed partial class SettingsScreenViewModel : ObservableObject, IScreen
{
    private readonly SettingsDependencies _deps;
    private readonly SettingsStore _store;

    /// <summary>True once the settings have been read; the sections show then.</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Why the settings could not be read, with a Try again button; null when they were.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private ErrorCardViewModel? _loadError;

    /// <param name="shell">What the screen tells about changed settings and follows for the layout.</param>
    /// <param name="dependencies">The Api and helpers.</param>
    public SettingsScreenViewModel(IShellContext shell, SettingsDependencies dependencies)
    {
        _deps = dependencies;
        _store = new SettingsStore(dependencies.Settings, dependencies.SettingsWriter, shell, dependencies.Toasts, dependencies.Text);
        Help = new HelpSectionViewModel(shell);
        SpaceHelp = ContextualHelp.Space(dependencies.Text, shell.ShowHelp);
        Storage = new StorageSectionViewModel(dependencies, _store);
        Appearance = new AppearanceSectionViewModel(_store, shell, dependencies.Text);
        Session = new SessionSectionViewModel(_store);
        ClearAll = new ClearAllDataSectionViewModel(dependencies, _store, shell);
        About = new AboutSectionViewModel(dependencies.Environment, dependencies.Text, dependencies.Toasts);
        AppsMenu = new AppsMenuSectionViewModel(dependencies.Environment.AppsMenu, dependencies.Text);
        RetryCommand = new AsyncRelayCommand(LoadAsync);

        // Subscribed for good, so a job that runs while another screen shows is still known when this one comes back.
        dependencies.Jobs.JobChanged += (_, job) => dependencies.Dispatcher.Post(() =>
        {
            Storage.Handle(job);
            ClearAll.Handle(job);
        });
    }

    /// <inheritdoc />
    public NavDestination Destination => NavDestination.Settings;

    /// <summary>The Help section, the first one on the screen.</summary>
    public HelpSectionViewModel Help { get; }

    /// <summary>"How much space do snapshots take?", under the snapshot folder.</summary>
    public ContextualHelpViewModel SpaceHelp { get; }

    /// <summary>The "Where SavePoint keeps your snapshots" section.</summary>
    public StorageSectionViewModel Storage { get; }

    /// <summary>The "Remove all SavePoint data" section.</summary>
    public ClearAllDataSectionViewModel ClearAll { get; }

    /// <summary>The Appearance section.</summary>
    public AppearanceSectionViewModel Appearance { get; }

    /// <summary>The "When you finish playing" section.</summary>
    public SessionSectionViewModel Session { get; }

    /// <summary>The About section.</summary>
    public AboutSectionViewModel About { get; }

    /// <summary>The "Apps menu" section, shown only when SavePoint runs from an AppImage.</summary>
    public AppsMenuSectionViewModel AppsMenu { get; }

    /// <summary>True when <see cref="LoadError"/> shows.</summary>
    public bool HasLoadError => LoadError is not null;

    /// <summary>Reads the settings again after a failure.</summary>
    public IAsyncRelayCommand RetryCommand { get; }

    /// <inheritdoc />
    public void OnShown() => _ = LoadAsync();

    /// <summary>Reads the stored settings and shows them; a read that fails shows an error card instead of the sections.</summary>
    public async Task LoadAsync()
    {
        var read = await _store.ReadAsync();
        if (!read.Ok)
        {
            IsLoaded = false;
            LoadError = new ErrorCardViewModel(read.Error, _deps.Text, [new ErrorCardAction(_deps.Text.Message("settings.retry"), RetryCommand, IsPrimary: true)]);
            return;
        }

        LoadError = null;
        About.Refresh();
        AppsMenu.Refresh();
        Help.Refresh();
        IsLoaded = true;

        // Long jobs are read after subscribing, so none that changes in between is missed.
        var jobs = _deps.Jobs.List();
        ClearAll.Seed(jobs);
        await Storage.LoadAsync(jobs);
    }
}
