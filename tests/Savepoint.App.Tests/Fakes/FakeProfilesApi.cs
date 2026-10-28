using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeProfilesApi : IProfilesApi
{
    private const string _api = nameof(IProfilesApi);

    private readonly FakeBackend _backend;

    internal FakeProfilesApi(FakeBackend backend)
    {
        _backend = backend;
    }

    private FakeState State => _backend.State;

    // A test sets these to make the reads take as long as it likes, the way a status scan of a large install does.
    public Func<CancellationToken, Task>? ListGate { get; set; }

    public Func<CancellationToken, Task>? StatsGate { get; set; }

    public async Task<OperationResult<IReadOnlyList<Profile>>> ListAsync(string? gameId = null, CancellationToken cancellationToken = default)
    {
        if (ListGate is { } gate)
        {
            await gate(cancellationToken);
        }

        return await _backend.Call(_api, nameof(ListAsync), [gameId], cancellationToken, () =>
            OperationResult.Success<IReadOnlyList<Profile>>([.. State.Profiles.Where(p => gameId is null || p.GameId == gameId)]));
    }

    // Running, last played, and the override are read live, so launches and edits show without re-seeding stats.
    public async Task<OperationResult<IReadOnlyList<ProfileRowStats>>> GetRowStatsAsync(string gameId, CancellationToken cancellationToken = default)
    {
        if (StatsGate is { } gate)
        {
            await gate(cancellationToken);
        }

        return await _backend.Call(_api, nameof(GetRowStatsAsync), [gameId], cancellationToken, () =>
        {
            State.Game(gameId);
            var running = State.IsOpen(gameId) ? State.Sessions[gameId].ProfileId : null;
            return OperationResult.Success<IReadOnlyList<ProfileRowStats>>(
                [
                    .. State.Profiles.Where(p => p.GameId == gameId).Select(p => State.StatsFor(p) with
                    {
                        IsRunning = p.Id == running,
                        LastPlayedAt = p.LastLaunchedAt ?? State.StatsFor(p).LastPlayedAt,
                        VersionOverride = p.VersionOverride,
                    }),
                ]);
        });
    }

    // The profile is listed at once and its capture job is left for the test to end.
    public Task<OperationResult<JobStarted>> CreateAsync(string gameId, string name, string? notes, string pointName, IReadOnlyCollection<RepoRole>? startEmpty = null) =>
        _backend.Call(_api, nameof(CreateAsync), [gameId, name, notes, pointName, startEmpty ?? []], () =>
        {
            var game = State.Game(gameId);
            var folders = FakeState.FoldersOf(game);

            // The same refusal as the real backend: the install folder never starts empty, and neither does a folder the game lacks.
            if ((startEmpty ?? []).Any(role => role == RepoRole.Install || !folders.Contains(role)))
            {
                throw new ValidationException([new ValidationIssue("startEmpty", ValidationMessageIds.ProfileStartEmptyInvalid)]);
            }

            // And a folder whose files no snapshot holds would lose them.
            if ((startEmpty ?? []).Any(role => !State.FolderHeld(gameId, role)))
            {
                throw new ValidationException([new ValidationIssue("startEmpty", ValidationMessageIds.ProfileStartEmptyUnsaved)]);
            }

            var shared = folders.Where(role => State.FolderHeld(gameId, role)).ToList();
            var profile = CatalogIds.NewProfile(gameId, name, State.Random) with { Notes = notes };
            State.Profiles.Add(profile);
            Load(profile);
            if (startEmpty?.Contains(RepoRole.Mods) == true)
            {
                State.RowStats[profile.Id] = State.StatsFor(profile) with { ModFileCount = 0 };
            }

            return OperationResult.Success(StartCapture(game, profile, pointName, folders, shared));
        });

    public Task<OperationResult<Profile>> UpdateAsync(string gameId, string profileId, ProfileChanges changes) =>
        _backend.Call(_api, nameof(UpdateAsync), [gameId, profileId, changes], () =>
        {
            var profile = State.Profile(gameId, profileId);
            var updated = profile with { Name = changes.Name, Notes = changes.Notes, VersionOverride = changes.VersionOverride };
            State.Profiles[State.Profiles.IndexOf(profile)] = updated;
            return OperationResult.Success(updated);
        });

    public Task<OperationResult<Profile>> DuplicateAsync(string gameId, string sourceProfileId, string name) =>
        _backend.Call(_api, nameof(DuplicateAsync), [gameId, sourceProfileId, name], () =>
        {
            var source = State.Profile(gameId, sourceProfileId);
            var copy = CatalogIds.NewProfile(gameId, name, State.Random) with { Notes = source.Notes, VersionOverride = source.VersionOverride };
            State.Profiles.Add(copy);
            State.RowStats[copy.Id] = State.StatsFor(source) with
            {
                ProfileId = copy.Id,
                IsActive = false,
                UnsavedChanges = [],
                LastPlayedAt = null,
                IsRunning = false,
                SnapshotNow = SnapshotNowAvailability.NotLoaded,
            };
            return OperationResult.Success(copy);
        });

    // The snapshot becomes the new profile's only snapshot and leaves the list, as in the real backend, where its record stays until space is freed up.
    public Task<OperationResult<Profile>> BringBackAsync(string gameId, HistoryPointId snapshotId, string name) =>
        _backend.Call(_api, nameof(BringBackAsync), [gameId, snapshotId, name], () =>
        {
            State.Game(gameId);
            var fresh = CatalogValidator.RequireValid(CatalogIds.NewProfile(gameId, name, State.Random));
            var index = State.Removed.FindIndex(r => r.GameId == gameId && r.Snapshot.Id == snapshotId);
            if (index < 0)
            {
                throw new RemovedSnapshotNotFoundException(snapshotId.Value);
            }

            var snapshot = State.Removed[index].Snapshot;
            var origin = State.Profiles.Find(p => p.GameId == gameId && p.Id == snapshot.FromProfileId);
            var profile = fresh with { VersionOverride = origin?.VersionOverride };
            State.Profiles.Add(profile);
            State.History.Add((gameId, new HistoryPoint(snapshot.Id, snapshot.Kind, snapshot.Name, snapshot.CapturedAt, profile.Id, profile.Name, snapshot.Install, null, null, false, false)));
            State.Removed.RemoveAt(index);
            return OperationResult.Success(profile);
        });

    // Unless scripted, the points lost are the ones this profile captured, counted per folder they touched (D17).
    public Task<OperationResult<ProfileDeletePreview>> GetDeletePreviewAsync(string gameId, string profileId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetDeletePreviewAsync), [gameId, profileId], cancellationToken, () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            if (State.DeletePreviews.TryGetValue(profileId, out var scripted))
            {
                return OperationResult.Success(scripted);
            }

            var points = State.PointsOf(gameId).Where(p => p.ProfileId == profileId).ToList();
            var folders = new List<FolderPointLoss> { new(RepoRole.Install, points.Count(p => p.Kind != HistoryPointKind.SaveOnly), null) };
            if (game.Mods is not null)
            {
                // A point that holds the version and mods holds the mods folder too; a save-only point does not.
                folders.Add(new FolderPointLoss(RepoRole.Mods, points.Count(p => p.Kind != HistoryPointKind.SaveOnly), null));
            }

            if (game.External is not null)
            {
                folders.Add(new FolderPointLoss(RepoRole.External, points.Count(p => p.Kind != HistoryPointKind.VersionAndMods), null));
            }

            return OperationResult.Success(new ProfileDeletePreview(profileId, points.Count, folders, DeleteBlocker(game, profile)));
        });

    public Task<OperationResult<Unit>> DeleteAsync(string gameId, string profileId) =>
        _backend.Call(_api, nameof(DeleteAsync), [gameId, profileId], () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            if (DeleteBlocker(game, profile) is { } blocker)
            {
                return OperationResult.Failure<Unit>(blocker);
            }

            State.RemoveProfile(profile);
            return OperationResult.Success();
        });

    public Task<OperationResult<JobStarted>> CaptureAsync(string gameId, string profileId, string pointName, IReadOnlyCollection<RepoRole>? folders = null) =>
        _backend.Call(_api, nameof(CaptureAsync), [gameId, profileId, pointName, folders], () =>
        {
            var game = State.Game(gameId);
            var profile = State.Profile(gameId, profileId);
            var roles = folders ?? FakeState.FoldersOf(game);

            // A snapshot of the whole profile is refused when the row says it is not loaded, so the refusal and the row's menu never disagree
            // and no job starts, as in the real backend. The row knows loaded-ness per profile, not per folder, so a request for particular
            // folders (saving the changes a profile owns in one folder, retrying the folders that failed) is let through.
            if (folders is null && State.StatsFor(profile).SnapshotNow == SnapshotNowAvailability.NotLoaded)
            {
                throw new ProfileNotLoadedException(profile.Id, roles.FirstOrDefault(RepoRole.Install));
            }

            return OperationResult.Success(StartCapture(game, profile, pointName, roles));
        });

    // A new profile's snapshot is of what the folders hold, so afterwards its profile is the loaded one and the one that was loaded is not.
    private void Load(Profile profile)
    {
        foreach (var other in State.Profiles.Where(p => p.GameId == profile.GameId && p.Id != profile.Id))
        {
            if (State.StatsFor(other) is { SnapshotNow: SnapshotNowAvailability.Available } stats)
            {
                State.RowStats[other.Id] = stats with { SnapshotNow = SnapshotNowAvailability.NotLoaded };
            }
        }

        State.RowStats[profile.Id] = State.StatsFor(profile) with { SnapshotNow = SnapshotNowAvailability.Available };
    }

    // A folder some profile's snapshot already holds is not recorded again by the snapshot a new profile takes of the same files.
    private JobStarted StartCapture(GameDefinition game, Profile profile, string pointName, IEnumerable<RepoRole> folders, IReadOnlyCollection<RepoRole>? sharedWithEarlierSnapshots = null)
    {
        var saved = new CaptureResult(profile.Id, State.NewPointId(), [.. folders.Select(r => new FolderOutcome(r))]);
        // Named after its profile means the profile's first snapshot, which the real work words without repeating the name.
        var opening = pointName == profile.Name
            ? JobLine.ForStep(JobSteps.CaptureStartFirst, ("profile", profile.Name))
            : JobLine.ForStep(JobSteps.CaptureStart, ("profile", profile.Name), ("snapshot", pointName));
        return new JobStarted(_backend.Jobs.Start(
            JobKind.Capture,
            game.Id,
            saved,
            job => RecordCapture(game.Id, profile, pointName, sharedWithEarlierSnapshots ?? [], job),
            [opening],
            new JobProfile(profile.Id, profile.Name)));
    }

    // A finished capture adds its point to the profile's history and leaves that profile as the one the folders hold, as the real work does.
    internal void RecordCapture(string gameId, Profile profile, string pointName, IReadOnlyCollection<RepoRole> sharedWithEarlierSnapshots, JobSnapshot job)
    {
        if (job.Status is not (JobStatus.Succeeded or JobStatus.Partial)
            || job.Result is not CaptureResult { PointId: { } pointId } result
            || State.PointsOf(gameId).Any(point => point.Id == pointId))
        {
            return;
        }

        var recorded = result.Folders.Where(folder => folder.Ok && !sharedWithEarlierSnapshots.Contains(folder.Role)).Select(folder => folder.Role).ToList();
        var recordsSaves = recorded.Contains(RepoRole.External);
        var recordsInstall = recorded.Any(role => role != RepoRole.External);
        if (!recordsSaves && !recordsInstall)
        {
            return;
        }

        var kind = recordsSaves ? (recordsInstall ? HistoryPointKind.VersionModsAndSave : HistoryPointKind.SaveOnly) : HistoryPointKind.VersionAndMods;
        var install = State.PointsOf(gameId).LastOrDefault(point => point.ProfileId == profile.Id)?.Install ?? new InstallState(null, null);

        // The fake cannot measure a folder, so a point that recorded the install files says they came to a megabyte.
        var point = new HistoryPoint(pointId, kind, pointName, job.FinishedAt ?? _backend.Clock.Now, profile.Id, profile.Name, install, null, recordsInstall ? 1 << 20 : null, false, recordsSaves);
        State.History.Add((gameId, point));
        RefreshAfterCapture(gameId, profile);
    }

    // The saves folder holds the newest point that recorded saves; going back to a profile's newest install point changes nothing only while that profile is the loaded one.
    private void RefreshAfterCapture(string gameId, Profile loaded)
    {
        var points = State.History.Where(entry => entry.GameId == gameId).Select(entry => entry.Point).ToList();
        var newestWithSaves = points.LastOrDefault(point => point.Kind != HistoryPointKind.VersionAndMods);
        var newestInstallOfLoaded = points.LastOrDefault(point => point.ProfileId == loaded.Id && point.Kind != HistoryPointKind.SaveOnly);
        for (var i = 0; i < State.History.Count; i++)
        {
            if (State.History[i].GameId == gameId)
            {
                var point = State.History[i].Point;
                State.History[i] = (gameId, point with { LoadedNow = point == newestWithSaves, InUse = point == newestInstallOfLoaded });
            }
        }

        foreach (var profile in State.Profiles.Where(candidate => candidate.GameId == gameId))
        {
            State.RowStats[profile.Id] = State.StatsFor(profile) with
            {
                IsActive = profile.Id == loaded.Id,
                SnapshotCount = points.Count(point => point.ProfileId == profile.Id),
                UnsavedChanges = profile.Id == loaded.Id ? [] : State.StatsFor(profile).UnsavedChanges,
                SnapshotNow = profile.Id == loaded.Id ? SnapshotNowAvailability.Available : SnapshotNowAvailability.NotLoaded,
            };
        }
    }

    // A running game blocks every destructive step (D13.5); the active profile cannot go either.
    private ErrorInfo? DeleteBlocker(GameDefinition game, Profile profile)
    {
        if (State.IsOpen(game.Id))
        {
            return ErrorInfo.From(new GameRunningException(game.Id, game.Name));
        }

        return State.StatsFor(profile).IsActive ? ErrorInfo.From(new ProfileActiveException(profile.Id)) : null;
    }
}
