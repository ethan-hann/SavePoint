using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
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

    public override Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _backend.Jobs.Complete(jobId);
        return Task.FromResult(_backend.Jobs.Get(jobId)!);
    }
}

internal sealed class RealContractHarness : ContractHarness
{
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(3);

    private readonly CoreBackend _backend;

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
        });
    }

    public override IGamesApi Games => _backend.Games;

    public override IProfilesApi Profiles => _backend.Profiles;

    public override IHistoryApi History => _backend.History;

    public override ISettingsApi Settings => _backend.Settings;

    public override IJobsApi Jobs => _backend.Jobs;

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
