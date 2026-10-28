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

// Everything a launch view model needs, wired to the fake backend with one clock: the sample scenario's central time zone, so dates read as designed.
internal sealed class LaunchRig : IDisposable
{
    public static readonly TimeZoneInfo CentralDaylight = TimeZoneInfo.CreateCustomTimeZone("cdt", TimeSpan.FromHours(-5), "CDT", "CDT");

    public LaunchRig(FakeBackend? backend = null, IUiDispatcher? dispatcher = null)
    {
        Backend = backend ?? FakeBackend.Sample();
        Clock = new ManualTimeProvider(ScenarioBuilder.DefaultNow);
        Text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), Clock, "windows", CentralDaylight);
        Dialogs = new FakeDialogs();
        Dispatcher = dispatcher ?? new ImmediateUiDispatcher();
        Toasts = new ToastHostViewModel(Clock, Dispatcher);
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
            Dispatcher,
            Clock,
            Awaiter,
            Attention);
    }

    // What stands in for the UI thread: immediate unless a test passed a held or real one.
    public IUiDispatcher Dispatcher { get; }

    public RecordingAttention Attention { get; } = new();

    public static LaunchTarget SurvivalTip => new LaunchTarget.ProfileTip(SampleScenario.SurvivalId);

    public static LaunchTarget ModdedTip => new LaunchTarget.ProfileTip(SampleScenario.ModdedId);

    public static LaunchTarget VanillaTip => new LaunchTarget.ProfileTip(SampleScenario.VanillaId);

    public static LaunchTarget ClearedBlackreachPoint => new LaunchTarget.Point(SampleScenario.ClearedBlackreach);

    public FakeBackend Backend { get; }

    public ManualTimeProvider Clock { get; }

    public UiFormatter Text { get; }

    public FakeDialogs Dialogs { get; }

    public ToastHostViewModel Toasts { get; }

    public JobAwaiter Awaiter { get; }

    public LaunchDependencies Deps { get; }

    public async Task<LaunchPreview> PreviewAsync(LaunchTarget target, string gameId = SampleScenario.SkyrimId) =>
        FakeAssert.Ok(await Backend.Launch.GetPreviewAsync(gameId, target));

    public async Task<LaunchDialogViewModel> DialogAsync(LaunchTarget? target = null, bool skipConfirm = false, Func<LaunchPreview, LaunchPreview>? change = null, bool explainDiscard = false)
    {
        var preview = await PreviewAsync(target ?? SurvivalTip);
        preview = change?.Invoke(preview) ?? preview;
        return new LaunchDialogViewModel(preview, "Skyrim Special Edition", ProfileName(preview.ProfileId), skipConfirm, id => ProfileName(id), Deps, explainDiscard);
    }

    // Plays a session through the fake to its exit, so the options are the ones the service would offer for that start.
    public async Task<SessionState.Exited> ExitedAsync(LaunchTarget? target = null, string gameId = SampleScenario.SkyrimId, int newFiles = SampleScenario.NewSaveFiles, int installFiles = 0, IReadOnlyList<string>? leaveOutRules = null)
    {
        FakeAssert.Ok(await Backend.Launch.StartAsync(gameId, target ?? ModdedTip));
        Backend.Launch.ReportRunning(gameId, SampleScenario.RunningSince);
        Backend.Launch.ReportExited(gameId, SampleScenario.PlayDuration, newFiles, installFiles, leaveOutRules: leaveOutRules);
        return Assert.IsType<SessionState.Exited>(FakeAssert.Session(Backend, gameId).State);
    }

    public string ProfileName(string profileId) =>
        profileId switch
        {
            SampleScenario.ModdedId => "Modded playthrough - 7-2-26",
            SampleScenario.VanillaId => "Vanilla - latest update",
            SampleScenario.SurvivalId => "Survival test - 5-14-26",
            _ => profileId,
        };

    public async Task<AppSettings> SettingsAsync() => FakeAssert.Ok(await Backend.Settings.GetAsync());

    public void Dispose()
    {
        Toasts.Dispose();
        Awaiter.Dispose();
    }
}
