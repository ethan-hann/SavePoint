using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Launch;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// Everything a launch view model needs, wired to the fake backend with one clock: the mockup's central time zone, so dates read as drawn.
internal sealed class LaunchRig : IDisposable
{
    public static readonly TimeZoneInfo CentralDaylight = TimeZoneInfo.CreateCustomTimeZone("cdt", TimeSpan.FromHours(-5), "CDT", "CDT");

    public LaunchRig(FakeBackend? backend = null)
    {
        Backend = backend ?? FakeBackend.Mockup();
        Clock = new ManualTimeProvider(ScenarioBuilder.DefaultNow);
        Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), Clock, "windows", CentralDaylight);
        Dialogs = new FakeDialogs();
        Toasts = new ToastHostViewModel(Clock, new ImmediateUiDispatcher());
        Awaiter = new JobAwaiter(Backend.Jobs);
        Deps = new LaunchDependencies(
            Backend.Launch,
            Backend.Profiles,
            Backend.Settings,
            new SettingsWriter(Backend.Settings),
            Backend.Games,
            Backend.Jobs,
            Dialogs,
            Text,
            Toasts,
            new ImmediateUiDispatcher(),
            Clock,
            Awaiter);
    }

    public static LaunchTarget SurvivalTip => new LaunchTarget.ProfileTip(MockupScenario.SurvivalId);

    public static LaunchTarget ModdedTip => new LaunchTarget.ProfileTip(MockupScenario.ModdedId);

    public static LaunchTarget VanillaTip => new LaunchTarget.ProfileTip(MockupScenario.VanillaId);

    public static LaunchTarget ClearedBlackreachPoint => new LaunchTarget.Point(MockupScenario.ClearedBlackreach);

    public FakeBackend Backend { get; }

    public ManualTimeProvider Clock { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public JobAwaiter Awaiter { get; }

    public LaunchDependencies Deps { get; }

    public async Task<LaunchPreview> PreviewAsync(LaunchTarget target, string gameId = MockupScenario.SkyrimId) =>
        FakeAssert.Ok(await Backend.Launch.GetPreviewAsync(gameId, target));

    public async Task<LaunchDialogViewModel> DialogAsync(LaunchTarget? target = null, bool skipConfirm = false, Func<LaunchPreview, LaunchPreview>? change = null)
    {
        var preview = await PreviewAsync(target ?? SurvivalTip);
        preview = change?.Invoke(preview) ?? preview;
        return new LaunchDialogViewModel(preview, "Skyrim Special Edition", ProfileName(preview.ProfileId), skipConfirm, id => ProfileName(id), Deps);
    }

    // Plays a session through the fake to its exit, so the options are the ones the service would offer for that start.
    public async Task<SessionState.Exited> ExitedAsync(LaunchTarget? target = null, string gameId = MockupScenario.SkyrimId, int newFiles = MockupScenario.NewSaveFiles, int installFiles = 0)
    {
        FakeAssert.Ok(await Backend.Launch.StartAsync(gameId, target ?? ModdedTip));
        Backend.Launch.ReportRunning(gameId, MockupScenario.RunningSince);
        Backend.Launch.ReportExited(gameId, MockupScenario.PlayDuration, newFiles, installFiles);
        return Assert.IsType<SessionState.Exited>(FakeAssert.Session(Backend, gameId).State);
    }

    public string ProfileName(string profileId) =>
        profileId switch
        {
            MockupScenario.ModdedId => "Modded playthrough - 7-2-26",
            MockupScenario.VanillaId => "Vanilla - latest update",
            MockupScenario.SurvivalId => "Survival test - 5-14-26",
            _ => profileId,
        };

    public async Task<AppSettings> SettingsAsync() => FakeAssert.Ok(await Backend.Settings.GetAsync());

    public void Dispose() => Awaiter.Dispose();
}
