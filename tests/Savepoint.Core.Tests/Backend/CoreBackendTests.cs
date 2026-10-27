using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

public class CoreBackendTests
{
    [Fact]
    public void Creating_the_backend_touches_no_folder()
    {
        using var workspace = new BackendWorkspace();

        Assert.False(Directory.Exists(workspace.CatalogDirectory));
        Assert.False(Directory.Exists(workspace.DefaultStorageRoot));
    }

    [Fact]
    public void Disposing_the_backend_takes_its_git_registry_off_the_exit_list()
    {
        using var workspace = new BackendWorkspace();
        var registry = workspace.Backend.GitRegistry;
        var listedBefore = GitProcessRegistry.IsClosedAtExit(registry);

        workspace.Backend.Dispose();
        workspace.Backend.Dispose();

        Assert.True(listedBefore);
        Assert.False(GitProcessRegistry.IsClosedAtExit(registry));
    }

    [Fact]
    public void A_backend_that_fails_to_build_leaves_no_git_registry_on_the_exit_list()
    {
        var failing = false;
        GitProcessRegistry? made = null;
        using var workspace = new BackendWorkspace(options => failing
            ? options with
            {
                Launch = services =>
                {
                    made = services.Runner.Options.Registry;
                    throw new InvalidOperationException("the launch component could not be built");
                },
            }
            : options);
        failing = true;

        Assert.Throws<InvalidOperationException>(workspace.Restart);

        Assert.NotNull(made);
        Assert.False(GitProcessRegistry.IsClosedAtExit(made));
    }

    [Fact]
    public void Every_api_is_present()
    {
        using var workspace = new BackendWorkspace();
        var backend = workspace.Backend;

        Assert.NotNull(backend.Games);
        Assert.NotNull(backend.Profiles);
        Assert.NotNull(backend.Launch);
        Assert.NotNull(backend.Settings);
        Assert.NotNull(backend.History);
        Assert.NotNull(backend.Jobs);
        Assert.NotNull(backend.AppStatus);
    }

    [Fact]
    public void The_jobs_api_is_the_job_registry_the_services_share()
    {
        using var workspace = new BackendWorkspace();

        Assert.Same(workspace.Backend.Services.Jobs, workspace.Backend.Jobs);
    }

    [Fact]
    public void Nothing_is_running_until_sessions_are_supplied()
    {
        using var workspace = new BackendWorkspace(options => options with { Sessions = null });

        Assert.False(workspace.Backend.Services.Sessions.IsGameRunning("alpha"));
        Assert.Empty(workspace.Backend.Services.Sessions.RunningGameIds());
    }

    [Fact]
    public void The_sessions_given_in_the_options_answer_for_the_services()
    {
        using var workspace = new BackendWorkspace();
        workspace.Sessions.Start("alpha", "profile-1");

        var sessions = workspace.Backend.Services.Sessions;

        Assert.True(sessions.IsGameRunning("alpha"));
        Assert.Equal("profile-1", sessions.RunningProfileId("alpha"));
        Assert.Equal(["alpha"], sessions.RunningGameIds());
    }

    [Fact]
    public void A_launch_component_is_built_from_the_shared_services_and_its_sessions_take_over()
    {
        var launch = new RecordingLaunchApi();
        var sessions = new FakeSessions();
        BackendServices? seen = null;
        using var workspace = new BackendWorkspace(options => options with
        {
            Launch = services =>
            {
                seen = services;
                return new LaunchComponents(launch, sessions);
            },
        });
        sessions.Start("alpha", "profile-2");

        Assert.Same(launch, workspace.Backend.Launch);
        Assert.Same(workspace.Backend.Services, seen);
        Assert.True(workspace.Backend.Services.Sessions.IsGameRunning("alpha"));
        Assert.Equal("profile-2", workspace.Backend.Services.Sessions.RunningProfileId("alpha"));
    }

    [Fact]
    public void Changes_in_the_attached_sessions_are_announced_through_the_services()
    {
        var sessions = new FakeSessions();
        using var workspace = new BackendWorkspace(options => options with { Launch = _ => new LaunchComponents(new RecordingLaunchApi(), sessions) });
        var raised = 0;
        workspace.Backend.Services.Sessions.Changed += (_, _) => raised++;

        sessions.Start("alpha", "profile-1");
        sessions.End("alpha");

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task A_storage_root_that_is_not_a_path_is_reported_as_unreachable()
    {
        using var workspace = new BackendWorkspace();
        await workspace.Backend.Services.Catalog.SaveSettingsAsync(new AppSettings(StorageRoot: "relative/folder"), workspace.Token);

        var failure = await Assert.ThrowsAsync<StorageUnreachableException>(() => workspace.Backend.Services.Storage.GetAsync(workspace.Token));

        Assert.Equal("error.storage.unreachable", failure.MessageId);
    }

    private sealed class RecordingLaunchApi : ILaunchApi
    {
        public event EventHandler<LaunchSessionInfo>? SessionChanged
        {
            add { }
            remove { }
        }

        public LaunchSessionInfo? GetSession(string gameId) => null;

        public IReadOnlyList<LaunchSessionInfo> ListSessions() => [];

        public Task<OperationResult<LaunchPreview>> GetPreviewAsync(string gameId, LaunchTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<JobStarted>> StartAsync(string gameId, LaunchTarget target, bool startSteam = false) => throw new NotSupportedException();

        public Task<OperationResult<JobStarted>> SwitchAsync(string gameId, LaunchTarget target) => throw new NotSupportedException();

        public Task<OperationResult<Unit>> DiscardChangesAsync(string gameId, RepoRole folder) => throw new NotSupportedException();

        public Task<OperationResult<Unit>> MarkFinishedAsync(string gameId) => throw new NotSupportedException();

        public Task<OperationResult<Unit>> CompleteSessionAsync(string gameId, SessionCompletion completion) => throw new NotSupportedException();

        public Task<OperationResult<Unit>> RetryCompletionAsync(string gameId) => throw new NotSupportedException();
    }
}
