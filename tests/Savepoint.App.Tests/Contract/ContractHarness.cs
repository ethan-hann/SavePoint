using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.App.Tests.Contract;

// One backend the contract scenario drives: the fake or the real one, over the same game folders on disk.
internal abstract class ContractHarness : IAsyncDisposable
{
    protected ContractHarness()
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-contract-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public abstract IGamesApi Games { get; }

    public abstract IProfilesApi Profiles { get; }

    public abstract IHistoryApi History { get; }

    public abstract ISettingsApi Settings { get; }

    public abstract IJobsApi Jobs { get; }

    public abstract ILaunchApi Launch { get; }

    // Tells the backend which profile is about to be launched, so a fake can be scripted with what the real one works out for itself.
    public virtual void ExpectLaunch(GameDefinition game, string profileId)
    {
    }

    // Lets the game that StartAsync began run, and returns once the session says it does.
    public abstract Task GameRunningAsync(string gameId, CancellationToken cancellationToken);

    // Closes the running game, and returns once the session says it has exited.
    public abstract Task GameExitedAsync(string gameId, CancellationToken cancellationToken);

    // Ends the job the way the backend does: the fake is told to finish it, the real one is waited for.
    public abstract Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken);

    // A game with an install folder and a saves folder holding a few files.
    public GameDefinition NewGame(string name)
    {
        var id = name.ToLowerInvariant() + "-1";
        var install = Path.Combine(Root, "games", id, "game");
        var saves = Path.Combine(Root, "games", id, "saves");
        Directory.CreateDirectory(Path.Combine(install, "data"));
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(install, "game.exe"), "MZ not a real program");
        File.WriteAllText(Path.Combine(install, "data", "base.pak"), "base content");
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "save one");
        return new GameDefinition(
            Id: id,
            Name: name,
            Install: new InstallLocation(install),
            External: new ExternalLocation(saves),
            Launch: new LaunchConfig(LaunchMethod.Exe, "game.exe"));
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await Task.Run(DeleteRoot).ConfigureAwait(false);
    }

    // Git writes its object files read-only, which a plain recursive delete cannot remove on Windows.
    private void DeleteRoot()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class FakeContractHarness : ContractHarness
{
    private readonly FakeBackend _backend = FakeBackend.Empty();

    public override IGamesApi Games => _backend.Games;

    public override IProfilesApi Profiles => _backend.Profiles;

    public override IHistoryApi History => _backend.History;

    public override ISettingsApi Settings => _backend.Settings;

    public override IJobsApi Jobs => _backend.Jobs;

    public override ILaunchApi Launch => _backend.Launch;

    public override void ExpectLaunch(GameDefinition game, string profileId)
    {
        var target = new LaunchTarget.ProfileTip(profileId);
        var state = new InstallState(null, null);
        var save = new SaveEntry(new HistoryPointId("point-1"), "Fresh install", DateTimeOffset.UnixEpoch, IsMostRecent: true);
        _backend.State.LaunchPreviews[(game.Id, target)] = new LaunchPreview(
            game.Id,
            profileId,
            target,
            new FolderLaunchState(RepoRole.Install, game.Install.Path, true, false, 0, null, null),
            new FolderLaunchState(RepoRole.External, game.External!.Path, true, false, 0, null, null),
            state,
            state,
            [save],
            save.Id,
            ShowBanner: true,
            OnlySaveDiffers: false,
            TargetIsOlderPoint: false);
    }

    public override Task GameRunningAsync(string gameId, CancellationToken cancellationToken)
    {
        _backend.Launch.ReportRunning(gameId);
        return Task.CompletedTask;
    }

    public override Task GameExitedAsync(string gameId, CancellationToken cancellationToken)
    {
        _backend.Launch.ReportExited(gameId, TimeSpan.FromMinutes(5), newFileCount: 0);
        return Task.CompletedTask;
    }

    public override Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _backend.Jobs.Complete(jobId);
        return Task.FromResult(_backend.Jobs.Get(jobId)!);
    }
}

internal sealed class RealContractHarness : ContractHarness
{
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(5);

    private readonly CoreBackend _backend;
    private readonly ScriptedGame _game = new();

    public RealContractHarness()
    {
        var home = Path.Combine(Root, "home");
        var install = Path.Combine(Root, "app");
        Directory.CreateDirectory(install);
        _backend = CoreBackend.Create(new BackendOptions
        {
            Platform = HostPlatform(home),
            InstallDirectory = install,
            Git = GitBinary.Locate(Path.Combine(Root, "gitconfig")),
            LooseObjectThreshold = int.MaxValue,
            Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Starter = _game.Starter(TimeProvider.System) }),
        });
    }

    public override IGamesApi Games => _backend.Games;

    public override IProfilesApi Profiles => _backend.Profiles;

    public override IHistoryApi History => _backend.History;

    public override ISettingsApi Settings => _backend.Settings;

    public override IJobsApi Jobs => _backend.Jobs;

    public override ILaunchApi Launch => _backend.Launch;

    public override Task GameRunningAsync(string gameId, CancellationToken cancellationToken) =>
        WaitForSessionAsync(gameId, state => state is SessionState.Running, cancellationToken);

    public override Task GameExitedAsync(string gameId, CancellationToken cancellationToken)
    {
        _game.ExitAll();
        return WaitForSessionAsync(gameId, state => state is SessionState.Exited, cancellationToken);
    }

    // The real session moves on its own threads, so the story waits for the phase it needs.
    private async Task WaitForSessionAsync(string gameId, Func<SessionState, bool> reached, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + _limit;
        while (_backend.Launch.GetSession(gameId) is not { } session || !reached(session.State))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"The session never reached the phase the story waits for; it is {_backend.Launch.GetSession(gameId)?.State.GetType().Name ?? "missing"}.");
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<JobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, JobSnapshot snapshot)
        {
            if (snapshot.JobId == jobId && snapshot.Status != JobStatus.Running)
            {
                finished.TrySetResult(snapshot);
            }
        }

        _backend.Jobs.JobChanged += OnChanged;
        try
        {
            if (_backend.Jobs.Get(jobId) is { Status: not JobStatus.Running } already)
            {
                return already;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_limit);
            await using var registration = deadline.Token.Register(() => finished.TrySetCanceled());
            return await finished.Task.ConfigureAwait(false);
        }
        finally
        {
            _backend.Jobs.JobChanged -= OnChanged;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        // Work queued behind a call, such as the cleanup after a profile delete, must end before its folders go.
        var until = DateTime.UtcNow + _limit;
        while (_backend.Jobs.List().Any(job => job.Status == JobStatus.Running) && DateTime.UtcNow < until)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    // The running OS's platform over a home inside the throwaway folder, so nothing real is touched.
    private static IPlatform HostPlatform(string home)
    {
        var environment = new PlatformEnvironment { HomeDirectory = home };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.Linux => new LinuxPlatform(environment),
            _ => new MacOSPlatform(environment),
        };
    }
}
