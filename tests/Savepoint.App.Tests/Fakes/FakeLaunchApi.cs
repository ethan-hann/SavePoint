using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.App.Tests.Fakes;

// The session and its launch job move together: ending the job (by script or cancel) moves the session, so one step drives both events.
public sealed class FakeLaunchApi : ILaunchApi
{
    private const string _api = nameof(ILaunchApi);

    private readonly FakeBackend _backend;
    private readonly Dictionary<string, DateTimeOffset> _startedAt = [];
    private readonly HashSet<string> _switching = [];
    private long _revision;

    internal FakeLaunchApi(FakeBackend backend)
    {
        _backend = backend;
    }

    public event EventHandler<LaunchSessionInfo>? SessionChanged;

    private FakeState State => _backend.State;

    public LaunchSessionInfo? GetSession(string gameId) =>
        _backend.Read(_api, nameof(GetSession), [gameId], () => State.Sessions.GetValueOrDefault(gameId));

    public IReadOnlyList<LaunchSessionInfo> ListSessions() =>
        _backend.Read<IReadOnlyList<LaunchSessionInfo>>(_api, nameof(ListSessions), [], () => [.. State.Sessions.Values]);

    // A test sets this to make the scan take as long as it likes; a cancelled token ends it with the cancelled error, as the real service does.
    public Func<CancellationToken, Task>? PreviewGate { get; set; }

    public async Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default)
    {
        if (PreviewGate is { } gate)
        {
            try
            {
                await gate(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return OperationResult.Failure<LaunchPreview>(ErrorInfo.From(new RequestCancelledException()));
            }
        }

        return await _backend.Call(_api, nameof(GetPreviewAsync), [gameId, target], () =>
            State.LaunchPreviews.TryGetValue((gameId, target), out var preview)
                ? OperationResult.Success(preview)
                : throw new KeyNotFoundException($"No launch preview is scripted for {target} in game '{gameId}'."));
    }

    public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target, bool startSteam = false) =>
        _backend.Call(_api, nameof(StartAsync), startSteam ? [gameId, target, true] : [gameId, target], () =>
        {
            State.Game(gameId);
            if (Taken(gameId) is { } taken)
            {
                return OperationResult.Failure<JobStarted>(taken);
            }

            var profileId = ProfileOf(gameId, target);
            var older = State.LaunchPreviews.TryGetValue((gameId, target), out var preview) && preview.TargetIsOlderPoint;
            var jobId = _backend.Jobs.Start(JobKind.Launch, gameId, null, job => OnLaunchJobFinished(gameId, job), profile: JobProfileOf(profileId));
            Publish(new LaunchSessionInfo(gameId, profileId, target, older, jobId, new SessionState.Preparing(), 0));
            return OperationResult.Success(new JobStarted(jobId));
        });

    // As the real one: a switch job and no session, with the game taken from the start of the job until it ends.
    public Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target) =>
        _backend.Call(_api, nameof(SwitchAsync), [gameId, target], () =>
        {
            State.Game(gameId);
            if (Taken(gameId) is { } taken)
            {
                return OperationResult.Failure<JobStarted>(taken);
            }

            _switching.Add(gameId);
            var profileId = ProfileOf(gameId, target);
            var jobId = _backend.Jobs.Start(JobKind.Switch, gameId, null, job => OnSwitchJobFinished(gameId, profileId, job), profile: JobProfileOf(profileId));
            return OperationResult.Success(new JobStarted(jobId));
        });

    public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) =>
        _backend.Call(_api, nameof(DiscardChangesAsync), [gameId, folder], () =>
        {
            State.Game(gameId);

            // As the real one: not while a session is open (including one waiting for the player's choice), since the game may be writing.
            if (_switching.Contains(gameId))
            {
                return OperationResult.Failure<Unit>(new SwitchInProgressException(gameId, State.Game(gameId).Name));
            }

            if (State.IsOpen(gameId))
            {
                return OperationResult.Failure<Unit>(new GameRunningException(gameId, State.Game(gameId).Name));
            }

            foreach (var profile in State.Profiles.Where(p => p.GameId == gameId))
            {
                var stats = State.StatsFor(profile);
                State.RowStats[profile.Id] = stats with { UnsavedChanges = [.. stats.UnsavedChanges.Where(r => r != folder)] };
            }

            foreach (var key in State.LaunchPreviews.Keys.Where(k => k.GameId == gameId).ToList())
            {
                var preview = State.LaunchPreviews[key];
                State.LaunchPreviews[key] = preview with { Folders = [.. preview.Folders.Select(state => Clean(state, folder))] };
            }

            return OperationResult.Success();
        });

    public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) =>
        _backend.Call(_api, nameof(MarkFinishedAsync), [gameId], () =>
            State.IsOpen(gameId) ? OperationResult.Success() : OperationResult.Failure<Unit>(new NoActiveSessionException(gameId)));

    public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) =>
        _backend.Call(_api, nameof(CompleteSessionAsync), [gameId, completion], () =>
        {
            if (!State.Sessions.TryGetValue(gameId, out var session)
                || session.State is SessionState.Completing or SessionState.Completed or SessionState.Failed)
            {
                return OperationResult.Failure<Unit>(new NoActiveSessionException(gameId));
            }

            if (session.State is not SessionState.Exited)
            {
                return OperationResult.Failure<Unit>(new SessionStillRunningException(gameId));
            }

            var choice = completion switch
            {
                SessionCompletion.CaptureNew => CompletionChoice.CaptureNew,
                SessionCompletion.UpdateExisting => CompletionChoice.UpdateExisting,
                SessionCompletion.ForkNewProfile => CompletionChoice.ForkNewProfile,
                SessionCompletion.MoveForward => CompletionChoice.MoveForward,
                _ => CompletionChoice.Skip,
            };
            if (!((SessionState.Exited)session.State).Options.Choices.Contains(choice))
            {
                return OperationResult.Failure<Unit>(new CompletionNotOfferedException(gameId, choice));
            }

            if (completion is SessionCompletion.Skip)
            {
                Publish(session with { State = new SessionState.Completed(completion, null, null) });
                return OperationResult.Success();
            }

            var game = State.Game(gameId);
            var profileId = session.ProfileId;
            if (completion is SessionCompletion.ForkNewProfile fork)
            {
                var created = CatalogIds.NewProfile(gameId, fork.ProfileName, State.Random);
                State.Profiles.Add(created);
                profileId = created.Id;
            }

            var saved = new CaptureResult(profileId, State.NewPointId(), [.. FakeState.FoldersOf(game).Select(r => new FolderOutcome(r))]);
            var jobId = _backend.Jobs.Start(JobKind.Capture, gameId, saved, job => OnCompletionFinished(gameId, completion, job), CompletionOpening(profileId, completion), JobProfileOf(profileId));
            Publish(session with { State = new SessionState.Completing(completion, jobId) });
            return OperationResult.Success();
        });

    public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) =>
        _backend.Call(_api, nameof(RetryCompletionAsync), [gameId], () =>
        {
            if (!State.Sessions.TryGetValue(gameId, out var session)
                || session.State is not SessionState.Completed { Completion: not SessionCompletion.Skip } completed
                || (completed.Error is null && completed.Result?.Folders.All(folder => folder.Ok) != false))
            {
                return OperationResult.Failure<Unit>(new NoActiveSessionException(gameId));
            }

            // Only the folders that did not save run again; the job's scripted result joins them to the ones that did.
            var game = State.Game(gameId);
            var earlier = completed.Result?.Folders.Where(folder => folder.Ok).ToList() ?? [];
            var missing = FakeState.FoldersOf(game).Where(role => earlier.All(folder => folder.Role != role));
            var saved = new CaptureResult(completed.Result?.ProfileId ?? session.ProfileId, completed.Result?.PointId ?? State.NewPointId(), [.. earlier, .. missing.Select(role => new FolderOutcome(role))]);
            var jobId = _backend.Jobs.Start(JobKind.Capture, gameId, saved, job => OnCompletionFinished(gameId, completed.Completion, job), CompletionOpening(saved.ProfileId, completed.Completion), JobProfileOf(saved.ProfileId));
            Publish(session with { State = new SessionState.Completing(completed.Completion, jobId) });
            return OperationResult.Success();
        });

    public void ReportSwitching(string gameId, JobProgress progress) =>
        _backend.Script(() =>
        {
            var session = Require(gameId, nameof(ReportSwitching), s => s is SessionState.Preparing or SessionState.Switching);
            _backend.Jobs.ApplyProgress(session.LaunchJobId, progress);
            Publish(session with { State = new SessionState.Switching(progress) });
        });

    // Ends the launch job, which is what moves the session to Running.
    public void ReportRunning(string gameId, DateTimeOffset? startedAt = null) =>
        _backend.Script(() =>
        {
            var session = Require(gameId, nameof(ReportRunning), s => s is SessionState.Preparing or SessionState.Switching);
            _startedAt[gameId] = startedAt ?? _backend.Clock.Now;
            _backend.Jobs.ApplyLine(session.LaunchJobId, JobLine.ForStep(JobSteps.LaunchStarting, ("game", State.Game(gameId).Name)));
            _backend.Jobs.Finish(session.LaunchJobId, null);
        });

    // The started program closed with an error while the session still waits for the game (or the game showed up, with null).
    public void ReportProgramExit(string gameId, int? exitCode) =>
        _backend.Script(() =>
        {
            var session = Require(gameId, nameof(ReportProgramExit), s => s is SessionState.Running);
            Publish(session with { State = (SessionState.Running)session.State with { ProgramExitCode = exitCode } });
        });

    public void FailLaunch(string gameId, ErrorInfo error) =>
        _backend.Script(() =>
        {
            var session = Require(gameId, nameof(FailLaunch), s => s is SessionState.Preparing or SessionState.Switching);
            _backend.Jobs.FinishWith(session.LaunchJobId, JobStatus.Failed, null, error);
        });

    // Without options, the choices follow D14: an older start forks by default, otherwise a new point is the default.
    public void ReportExited(string gameId, TimeSpan playDuration, int newFileCount, int changedInstallFileCount = 0, PostPlayOptions? options = null, SessionEndKind endKind = SessionEndKind.ChildExited, IReadOnlyList<string>? leaveOutRules = null, int? programExitCode = null, int changedModsFileCount = 0) =>
        _backend.Script(() =>
        {
            var session = Require(gameId, nameof(ReportExited), s => s is SessionState.Running);
            var running = (SessionState.Running)session.State;
            var exited = new SessionState.Exited(running.StartedAt, playDuration, newFileCount, changedInstallFileCount, options ?? DefaultOptions(session), endKind, leaveOutRules, programExitCode, changedModsFileCount);
            Publish(session with { State = exited });
        });

    // Why a start or a switch cannot begin: a switch holds the game, or a session is open.
    private Exception? Taken(string gameId) =>
        _switching.Contains(gameId) ? new SwitchInProgressException(gameId, State.Game(gameId).Name)
        : State.IsOpen(gameId) ? new LaunchAlreadyRunningException(gameId)
        : null;

    private static FolderLaunchState Clean(FolderLaunchState state, RepoRole folder) =>
        state.Role == folder ? state with { Dirty = false, DirtyFileCount = 0, DirtyOwnerProfileId = null } : state;

    private string ProfileOf(string gameId, LaunchTarget target) => target switch
    {
        LaunchTarget.ProfileTip tip => State.Profile(gameId, tip.ProfileId).Id,
        LaunchTarget.Save save => State.Profile(gameId, save.ProfileId).Id,
        LaunchTarget.Point point => State.Point(gameId, point.PointId).ProfileId,
        _ => throw new InvalidOperationException($"Unknown launch target {target}."),
    };

    private PostPlayOptions DefaultOptions(LaunchSessionInfo session)
    {
        var points = State.PointsOf(session.GameId).Where(p => p.ProfileId == session.ProfileId).ToList();
        if (session.StartedFromOlderPoint)
        {
            var from = StartPointTime(session);
            var newer = from is null ? 0 : points.Count(p => p.CapturedAt > from);
            return new PostPlayOptions([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], CompletionChoice.ForkNewProfile, null, newer);
        }

        var latestSave = points.Where(p => p.Kind != HistoryPointKind.VersionAndMods).MaxBy(p => p.CapturedAt);
        if (latestSave is null)
        {
            return new PostPlayOptions([CompletionChoice.CaptureNew, CompletionChoice.Skip], CompletionChoice.CaptureNew, null, 0);
        }

        var update = new SaveEntry(latestSave.Id, latestSave.Name, latestSave.CapturedAt, true);
        return new PostPlayOptions([CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip], CompletionChoice.CaptureNew, update, 0);
    }

    private DateTimeOffset? StartPointTime(LaunchSessionInfo session)
    {
        var pointId = session.Target switch
        {
            LaunchTarget.Point point => point.PointId,
            LaunchTarget.Save save => save.SaveId,
            _ => (HistoryPointId?)null,
        };
        return pointId is { } id && State.PointsOf(session.GameId).FirstOrDefault(p => p.Id == id) is { } found ? found.CapturedAt : null;
    }

    private void OnLaunchJobFinished(string gameId, JobSnapshot job)
    {
        if (!State.Sessions.TryGetValue(gameId, out var session) || session.LaunchJobId != job.JobId)
        {
            return;
        }

        if (job.Status != JobStatus.Succeeded)
        {
            var error = job.Error ?? ErrorInfo.From(new InvalidOperationException("The launch job ended without an error."));
            Publish(session with { State = new SessionState.Failed(error) });
            return;
        }

        var startedAt = _startedAt.Remove(gameId, out var at) ? at : _backend.Clock.Now;
        var index = State.Profiles.FindIndex(p => p.Id == session.ProfileId);
        if (index >= 0)
        {
            State.Profiles[index] = State.Profiles[index] with { LastLaunchedAt = startedAt };
        }

        Publish(session with { State = new SessionState.Running(startedAt) });
    }

    // The game is let go, and a switch that worked leaves the folders holding the target: only that profile reads as loaded now.
    private void OnSwitchJobFinished(string gameId, string profileId, JobSnapshot job)
    {
        _switching.Remove(gameId);
        if (job.Status != JobStatus.Succeeded)
        {
            return;
        }

        foreach (var profile in State.Profiles.Where(candidate => candidate.GameId == gameId))
        {
            State.RowStats[profile.Id] = State.StatsFor(profile) with { IsActive = profile.Id == profileId };
        }
    }

    private JobProfile? JobProfileOf(string profileId) =>
        State.Profiles.FirstOrDefault(candidate => candidate.Id == profileId) is { } profile ? new JobProfile(profile.Id, profile.Name) : null;

    // The first step of the snapshot taken after play, as the real work writes it: the profile and what the snapshot is called.
    private IReadOnlyList<JobLine> CompletionOpening(string profileId, SessionCompletion completion)
    {
        var snapshot = completion switch
        {
            SessionCompletion.CaptureNew captured => captured.PointName,
            SessionCompletion.UpdateExisting updated => updated.PointName,
            SessionCompletion.ForkNewProfile forked => forked.PointName,
            SessionCompletion.MoveForward moved => moved.PointName,
            _ => string.Empty,
        };
        var profile = State.Profiles.FirstOrDefault(candidate => candidate.Id == profileId)?.Name ?? string.Empty;
        return [JobLine.ForStep(JobSteps.CaptureStart, ("profile", profile), ("snapshot", snapshot))];
    }

    private void OnCompletionFinished(string gameId, SessionCompletion completion, JobSnapshot job)
    {
        if (!State.Sessions.TryGetValue(gameId, out var session) || session.State is not SessionState.Completing completing || completing.JobId != job.JobId)
        {
            return;
        }

        var error = job.Status is JobStatus.Failed or JobStatus.Cancelled ? job.Error : null;
        Publish(session with { State = new SessionState.Completed(completion, job.Result as CaptureResult, error) });
    }

    private LaunchSessionInfo Require(string gameId, string step, Func<SessionState, bool> allowed)
    {
        if (!State.Sessions.TryGetValue(gameId, out var session) || !allowed(session.State))
        {
            var phase = session?.State.GetType().Name ?? "no session";
            throw new InvalidOperationException($"{step} does not apply to game '{gameId}' in {phase}.");
        }

        return session;
    }

    private void Publish(LaunchSessionInfo session)
    {
        var next = session with { Revision = ++_revision };
        State.Sessions[next.GameId] = next;
        _backend.Raise(() => SessionChanged?.Invoke(this, next));
    }
}
