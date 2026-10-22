using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Settings;

// The real shell with the Settings screen the production factory builds, over the fake backend.
internal sealed class SettingsRig
{
    private SettingsRig(ShellRig shell, SettingsScreenViewModel screen, FakeBackend backend, FakeDialogs dialogs, ToastHostViewModel toasts, RecordingFileRevealer revealer, UiFormatter text)
    {
        ShellRig = shell;
        Screen = screen;
        Backend = backend;
        Dialogs = dialogs;
        Toasts = toasts;
        Revealer = revealer;
        Text = text;
    }

    public ShellRig ShellRig { get; }

    public ShellViewModel Shell => ShellRig.Shell;

    public RecordingThemeApplier Theme => ShellRig.Theme;

    public SettingsScreenViewModel Screen { get; }

    public FakeBackend Backend { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public RecordingFileRevealer Revealer { get; }

    public UiFormatter Text { get; }

    public static async Task<SettingsRig> StartAsync(
        FakeBackend? backend = null,
        string os = "windows",
        string? notices = null,
        string version = "1.2.3",
        Func<ISettingsApi, ISettingsApi>? settings = null,
        IArtworkProvider? artwork = null,
        string? logFolder = null)
    {
        backend ??= FakeBackend.Mockup();
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), backend.Clock, os, HistoryRig.MockupZone);
        var services = new AppServices(
            backend.Games,
            backend.Profiles,
            backend.Launch,
            settings is null ? backend.Settings : settings(backend.Settings),
            backend.History,
            backend.Jobs,
            backend.AppStatus,
            backend.Artwork);
        var dialogs = new FakeDialogs();
        var toasts = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
        var revealer = new RecordingFileRevealer();
        var context = new FeatureContext(services, dialogs, text, toasts, new ImmediateUiDispatcher());
        var factory = new CapturingFactory(new SettingsScreenFactory(context, new SettingsEnvironment(revealer, () => notices, version, logFolder)));
        var shell = await ShellRig.StartAsync(backend, screens: [factory], artwork: artwork);
        return new SettingsRig(shell, (SettingsScreenViewModel)factory.Created!, backend, dialogs, toasts, revealer, text);
    }

    public static async Task<SettingsRig> ShownAsync(FakeBackend? backend = null, string os = "windows", string? notices = null, string? logFolder = null)
    {
        var rig = await StartAsync(backend, os, notices, logFolder: logFolder);
        rig.Show();
        return rig;
    }

    public void Show() => Shell.Navigate(NavDestination.Settings);

    public int Calls<TApi>(string member) => Backend.Calls.To<TApi>(member).Count;

    private sealed class CapturingFactory(IScreenFactory inner) : IScreenFactory
    {
        public NavDestination Destination => inner.Destination;

        public IScreen? Created { get; private set; }

        public IScreen Create(IShellContext shell) => Created = inner.Create(shell);
    }
}

internal sealed class RecordingFileRevealer : IFileRevealer
{
    public List<string> Revealed { get; } = [];

    public bool Succeeds { get; set; } = true;

    public bool Reveal(string path)
    {
        Revealed.Add(path);
        return Succeeds;
    }
}
