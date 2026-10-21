using Savepoint.App.Hosting;
using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;

namespace Savepoint.App.Tests.Hosting;

public class AppBootstrapTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-bootstrap-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public async Task A_started_backend_gives_the_view_models_the_real_apis_and_a_file_revealer()
    {
        var started = AppBootstrap.Start(options => options with { Platform = HostPlatform(), Git = GitBinary.Locate(Path.Combine(_root, "gitconfig")) });

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
    public async Task Without_a_launch_component_the_launch_api_says_launching_is_unavailable()
    {
        var started = AppBootstrap.Start(options => options with { Platform = HostPlatform(), Git = GitBinary.Locate(Path.Combine(_root, "gitconfig")) });

        var launched = await started.Services.Launch.MarkFinishedAsync("any");

        Assert.Equal("error.launch.unavailable", launched.Error?.MessageId);
    }

    [Fact]
    public async Task A_backend_that_cannot_start_becomes_an_api_that_reports_why()
    {
        var started = AppBootstrap.Start(_ => throw new InvalidOperationException("This account has no home directory."));

        Assert.Null(started.Backend);
        Assert.NotNull(started.Failure);
        var status = await started.Services.AppStatus.CheckAsync(TestContext.Current.CancellationToken);
        Assert.Equal(started.Failure.MessageId, status.Error?.MessageId);
        Assert.Contains("no home directory", status.Error!.Detail, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
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
