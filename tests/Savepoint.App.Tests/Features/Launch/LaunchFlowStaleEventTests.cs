using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Launch;

// Events can arrive late or twice on a real thread pool; the flow keeps the newest state per session and ignores the rest.
public class LaunchFlowStaleEventTests : IDisposable
{
    private readonly LaunchRig _rig = new();
    private readonly ReplayableLaunchApi _launch;
    private readonly LaunchFlowViewModel _flow;
    private readonly List<LaunchSessionInfo> _seen = [];

    public LaunchFlowStaleEventTests()
    {
        _launch = new ReplayableLaunchApi(_rig.Backend.Launch);
        _launch.SessionChanged += (_, session) => _seen.Add(session);
        _flow = new LaunchFlowViewModel(_rig.Deps with { Launch = _launch });
    }

    public void Dispose()
    {
        _flow.Dispose();
        _rig.Dispose();
    }

    private async Task PlayWithProgressAsync()
    {
        FakeAssert.Ok(await _rig.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await _flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ClearedBlackreachPoint);
    }

    [Fact]
    public async Task An_older_state_arriving_late_does_not_undo_a_newer_one()
    {
        await PlayWithProgressAsync();
        var progress = _rig.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>().Single();
        _rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        var card = Assert.IsType<RunningCardViewModel>(_flow.InlineContentFor(SampleScenario.SkyrimId));
        Assert.True(progress.IsClosed);

        _launch.Raise(_seen[0]);

        Assert.Single(_rig.Dialogs.Shown.OfType<SwitchProgressDialogViewModel>());
        Assert.Same(card, _flow.InlineContentFor(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task The_same_state_arriving_twice_is_applied_once()
    {
        await PlayWithProgressAsync();
        _rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        var card = _flow.InlineContentFor(SampleScenario.SkyrimId);

        _launch.Raise(_seen[^1]);

        Assert.Same(card, _flow.InlineContentFor(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task An_old_sessions_ending_arriving_late_does_not_touch_the_new_session()
    {
        await _flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        _rig.Backend.Launch.FailLaunch(SampleScenario.SkyrimId, SampleScenario.FileInUseError);
        var failed = _seen[^1];
        await _flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        _rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        var card = _flow.InlineContentFor(SampleScenario.SkyrimId);

        _launch.Raise(failed);

        Assert.Same(card, _flow.InlineContentFor(SampleScenario.SkyrimId));
        Assert.True(_flow.IsActive(SampleScenario.SkyrimId));
    }

    [Fact]
    public async Task A_new_session_is_accepted_even_when_its_revision_is_lower_than_the_last_ones()
    {
        await _flow.BeginAsync(SampleScenario.SkyrimId, LaunchRig.ModdedTip);
        _rig.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        _rig.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, 1);
        var exited = _seen[^1];
        Assert.True(exited.Revision > 0);
        var fresh = new LaunchSessionInfo(
            SampleScenario.SatisfactoryId,
            SampleScenario.MainFactoryId,
            new LaunchTarget.ProfileTip(SampleScenario.MainFactoryId),
            false,
            Guid.NewGuid(),
            new SessionState.Running(SampleScenario.RunningSince),
            0);

        _launch.Raise(fresh);

        Assert.IsType<RunningCardViewModel>(_flow.InlineContentFor(SampleScenario.SatisfactoryId));
    }

    [Fact]
    public async Task A_session_that_is_already_over_and_was_never_started_here_is_ignored()
    {
        var finished = new LaunchSessionInfo(
            SampleScenario.SatisfactoryId,
            SampleScenario.MainFactoryId,
            new LaunchTarget.ProfileTip(SampleScenario.MainFactoryId),
            false,
            Guid.NewGuid(),
            new SessionState.Failed(SampleScenario.FileInUseError),
            9);

        _launch.Raise(finished);
        await Task.CompletedTask;

        Assert.Null(_flow.InlineContentFor(SampleScenario.SatisfactoryId));
        Assert.False(_flow.IsActive(SampleScenario.SatisfactoryId));
    }
}

// The fake's launch Api with the ability to deliver any session event again, as a late or duplicated one would arrive.
internal sealed class ReplayableLaunchApi : ILaunchApi
{
    private readonly ILaunchApi _inner;

    public ReplayableLaunchApi(ILaunchApi inner)
    {
        _inner = inner;
        _inner.SessionChanged += (_, session) => SessionChanged?.Invoke(this, session);
    }

    public event EventHandler<LaunchSessionInfo>? SessionChanged;

    public void Raise(LaunchSessionInfo session) => SessionChanged?.Invoke(this, session);

    public LaunchSessionInfo? GetSession(string gameId) => _inner.GetSession(gameId);

    public IReadOnlyList<LaunchSessionInfo> ListSessions() => _inner.ListSessions();

    public Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default) =>
        _inner.GetPreviewAsync(gameId, target, cancellationToken);

    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target, bool startSteam = false, bool withoutSteam = false) => _inner.StartAsync(gameId, target, startSteam, withoutSteam);

    public Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target) => _inner.SwitchAsync(gameId, target);

    public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) => _inner.DiscardChangesAsync(gameId, folder);

    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) => _inner.MarkFinishedAsync(gameId);

    public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) => _inner.CompleteSessionAsync(gameId, completion);

    public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) => _inner.RetryCompletionAsync(gameId);
}
