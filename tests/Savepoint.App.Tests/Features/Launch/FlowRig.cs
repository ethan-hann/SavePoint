using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// A launch flow over the fake backend with its events recorded, and helpers that drive a play session step by step.
internal sealed class FlowRig : IDisposable
{
    public const string Skyrim = MockupScenario.SkyrimId;

    public FlowRig(FakeBackend? backend = null)
    {
        Rig = new LaunchRig(backend);
        Flow = new LaunchFlowViewModel(Rig.Deps);
        Flow.InlineContentChanged += (_, gameId) => InlineChanges.Add(gameId);
        Flow.EditGameRequested += (_, request) => EditRequests.Add(request);
        Flow.SettingsSaved += (_, settings) => SavedSettings.Add(settings);
    }

    public LaunchRig Rig { get; }

    public LaunchFlowViewModel Flow { get; }

    public FakeBackend Backend => Rig.Backend;

    public FakeDialogs Dialogs => Rig.Dialogs;

    public List<string> InlineChanges { get; } = [];

    public List<EditGameRequest> EditRequests { get; } = [];

    public List<AppSettings> SavedSettings { get; } = [];

    public LaunchDialogViewModel LaunchDialog => Newest<LaunchDialogViewModel>();

    public SwitchProgressDialogViewModel Progress => Newest<SwitchProgressDialogViewModel>();

    public PostPlayDialogViewModel PostPlay => Newest<PostPlayDialogViewModel>();

    private T Newest<T>()
        where T : class
    {
        var shown = Dialogs.Shown.OfType<T>().ToList();
        Assert.NotEmpty(shown);
        return shown[^1];
    }

    public static ErrorCardAction Action(ErrorCardViewModel card, string label) =>
        card.Actions.Single(action => action.Label == label);

    public static void Press(ErrorCardViewModel card, string label) => Action(card, label).Command.Execute(null);

    public T Inline<T>(string gameId = Skyrim)
        where T : class => Assert.IsType<T>(Flow.InlineContentFor(gameId));

    public IReadOnlyList<ApiCall> Starts() => Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.StartAsync));

    public IReadOnlyList<ApiCall> Completes() => Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.CompleteSessionAsync));

    public Task Play(LaunchTarget target, string gameId = Skyrim) => Flow.BeginAsync(gameId, target);

    // The banner start: Modded is already in place, so the game starts at once and the session is Preparing.
    public async Task PlayModdedAsync()
    {
        await Play(LaunchRig.ModdedTip);
    }

    // The progress-dialog start: going back to a point with only the saves different, with the skip preference on.
    public async Task PlayWithProgressAsync()
    {
        FakeAssert.Ok(await Backend.Settings.SaveAsync(MockupScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await Play(LaunchRig.ClearedBlackreachPoint);
    }

    public async Task PlayToRunningAsync()
    {
        await PlayModdedAsync();
        Backend.Launch.ReportRunning(Skyrim, MockupScenario.RunningSince);
    }

    public async Task PlayToExitedAsync(int newFiles = MockupScenario.NewSaveFiles, int installFiles = 0)
    {
        await PlayToRunningAsync();
        Backend.Launch.ReportExited(Skyrim, MockupScenario.PlayDuration, newFiles, installFiles);
    }

    public LaunchSessionInfo Session() => FakeAssert.Session(Backend, Skyrim);

    public void Dispose()
    {
        Flow.Dispose();
        Rig.Dispose();
    }

    public static ErrorInfo Spawn() => ErrorInfo.From(new LaunchSpawnException("exe", "could not start", null));

    public static ErrorInfo Cancelled() => ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Launch, Skyrim));

    public static IAsyncRelayCommand AsyncCommand(ErrorCardAction action) => Assert.IsAssignableFrom<IAsyncRelayCommand>(action.Command);
}
