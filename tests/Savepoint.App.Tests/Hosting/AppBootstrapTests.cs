using Savepoint.App.Hosting;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public class AppBootstrapTests : IDisposable
{
    private readonly RecordingDiagnosticLog _log = new();
    private readonly List<CoreBackend> _backends = [];
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-bootstrap-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task A_started_backend_gives_the_view_models_the_real_apis_and_a_file_revealer()
    {
        var started = StartOnHost();

        Assert.NotNull(started.Backend);
        Assert.Null(started.Failure);
        Assert.Same(started.Backend.Games, started.Services.Games);
        Assert.Same(started.Backend.AppStatus, started.Services.AppStatus);
        Assert.NotNull(started.Services.FileRevealer);
        var status = await started.Services.AppStatus.CheckAsync(TestContext.Current.CancellationToken);
        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Git.Available);
    }

    [Fact]
    public async Task The_started_backend_runs_the_real_launch_orchestrator()
    {
        var started = StartOnHost();

        var finished = await started.Services.Launch.MarkFinishedAsync("any");
        var preview = await started.Services.Launch.GetPreviewAsync("any", new LaunchTarget.ProfileTip("any"), TestContext.Current.CancellationToken);

        Assert.Equal("error.launch.noActiveSession", finished.Error?.MessageId);
        Assert.Equal("error.game.notFound", preview.Error?.MessageId);
    }

    [Fact]
    public void The_backend_is_started_with_the_artwork_handler_and_gets_its_artwork_api_through_the_services()
    {
        BackendOptions? seen = null;

        var started = StartOnHost(options => seen = options);

        Assert.IsType<ArtworkHttpHandler>(seen!.ArtworkHttp);
        Assert.Same(started.Backend!.Artwork, started.Services.Artwork);
    }

    [Fact]
    public async Task When_the_backend_cannot_start_the_artwork_api_reports_why_too()
    {
        var started = AppBootstrap.Start(_ => throw new InvalidOperationException("This account has no home directory."));

        Assert.NotNull(started.Services.Artwork);
        var art = await started.Services.Artwork.GetAsync("any", TestContext.Current.CancellationToken);

        Assert.Equal(started.Failure!.MessageId, art.Error?.MessageId);
    }

    public static TheoryData<Exception> StartFailures => new()
    {
        new InvalidOperationException("This account has no home directory."),
        new IOException("The configuration folder cannot be created."),
        new UnauthorizedAccessException("The configuration folder is read-only."),
        new StorageUnreachableException("/storage", null),
        new ArgumentException("The home directory is not a path."),
        new NotSupportedException("That path form is not supported."),
        new System.Security.SecurityException("Access was denied."),
    };

    [Theory]
    [MemberData(nameof(StartFailures))]
    public async Task A_backend_that_cannot_start_becomes_an_api_that_reports_why(Exception failure)
    {
        var started = AppBootstrap.Start(_ => throw failure);

        Assert.Null(started.Backend);
        Assert.NotNull(started.Failure);
        var status = await started.Services.AppStatus.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(started.Failure.MessageId, status.Error?.MessageId);
        Assert.Contains(failure.Message, status.Error!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_out_of_memory_is_not_turned_into_a_startup_screen()
    {
        Assert.Throws<OutOfMemoryException>(() => AppBootstrap.Start(_ => throw new OutOfMemoryException()));
    }

    [Fact]
    public void Only_an_api_that_failed_to_start_says_a_retry_cannot_help()
    {
        var failed = AppBootstrap.Start(_ => throw new IOException("no"));
        var started = StartOnHost();

        Assert.False(failed.Services.BackendStarted);
        Assert.True(started.Services.BackendStarted);
    }

    public void Dispose()
    {
        foreach (var backend in _backends)
        {
            backend.Dispose();
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // Starts the backend over the throwaway folder; the test's dispose ends the backend before the folder goes.
    private BootstrapResult StartOnHost(Action<BackendOptions>? seen = null)
    {
        var started = AppBootstrap.Start(options =>
        {
            seen?.Invoke(options);
            return options with { Platform = HostPlatform(), Git = GitBinary.Locate(Path.Combine(_root, "gitconfig")), ArtworkHttp = null };
        }, _log);
        if (started.Backend is not null)
        {
            _backends.Add(started.Backend);
        }

        return started;
    }

    // The running OS's platform over a home inside the throwaway folder, so nothing real is touched.
    private IPlatform HostPlatform()
    {
        var environment = new PlatformEnvironment { HomeDirectory = Path.Combine(_root, "home") };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.Linux => new LinuxPlatform(environment),
            _ => new MacOSPlatform(environment),
        };
    }
}
