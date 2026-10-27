using System.Runtime.InteropServices;
using Savepoint.App.Hosting;
using Savepoint.Core.Backend;
using Savepoint.Core.Git;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Hosting;

public sealed class AppBootstrapLogTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-bootstraplog-" + Guid.NewGuid().ToString("N")));
    private readonly RecordingDiagnosticLog _log = new();
    private BootstrapResult? _started;

    public void Dispose()
    {
        _started?.Backend?.Dispose();
        (_started?.Log as IDisposable)?.Dispose();
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

    private BackendOptions Over(BackendOptions options, IPlatform platform) =>
        options with { Platform = platform, Git = GitBinary.Locate(Path.Combine(_root, "gitconfig")), ArtworkHttp = null };

    [Fact]
    public void Starting_logs_the_app_version_and_the_system_it_runs_on()
    {
        var platform = HostPlatform();

        _started = AppBootstrap.Start(options => Over(options, platform), _log);

        var line = Assert.Single(_log.Containing("SavePoint", "started"));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(Savepoint.App.Features.Settings.SettingsEnvironment.ForThisApp().AppVersion, line.Message, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.OSDescription, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_logs_the_build_with_the_commit_it_was_made_from()
    {
        var platform = HostPlatform();

        _started = AppBootstrap.Start(options => Over(options, platform), _log, build: "0.9.0+3e0f9d9a1b2c");

        var line = Assert.Single(_log.Containing("SavePoint", "started"));
        Assert.Contains("SavePoint 0.9.0+3e0f9d9a1b2c started on", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_backend_writes_to_the_log_it_was_started_with()
    {
        var platform = HostPlatform();
        _started = AppBootstrap.Start(options => Over(options, platform), _log);

        await _started.Services.AppStatus.CheckAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(_log.Containing("Startup check: git"));
    }

    [Fact]
    public void The_services_say_where_the_log_folder_is()
    {
        var platform = HostPlatform();

        _started = AppBootstrap.Start(options => Over(options, platform), _log);

        Assert.True(_started.Services.BackendStarted);
        Assert.Equal(platform.Directories.LogsDir, _started.Services.LogFolder);
    }

    [Fact]
    public void A_backend_that_cannot_start_is_logged_with_its_exception_before_the_result_comes_back()
    {
        var platform = HostPlatform();
        var failure = new InvalidOperationException("The launch component could not be built.");

        _started = AppBootstrap.Start(options => Over(options, platform) with { Launch = _ => throw failure }, _log);

        Assert.False(_started.Services.BackendStarted);
        var line = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("could not start", line.Message, StringComparison.Ordinal);
        Assert.Contains(failure.Message, line.Message, StringComparison.Ordinal);
        Assert.Same(failure, line.Exception);
        Assert.Equal(platform.Directories.LogsDir, _started.Services.LogFolder);
    }

    [Fact]
    public void A_failure_before_any_folder_is_known_is_still_logged_and_offers_no_folder()
    {
        var failure = new IOException("The configuration folder cannot be created.");

        _started = AppBootstrap.Start(_ => throw failure, _log);

        var line = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Same(failure, line.Exception);
        Assert.Null(_started.Services.LogFolder);
    }

    [Fact]
    public void Without_a_log_the_real_one_is_opened_in_the_platforms_log_folder_and_its_first_line_is_the_start()
    {
        var platform = HostPlatform();
        Assert.StartsWith(_root, platform.Directories.LogsDir, StringComparison.Ordinal);

        _started = AppBootstrap.Start(options => Over(options, platform));

        var real = Assert.IsType<SerilogDiagnosticLog>(_started.Log);
        Assert.Equal(platform.Directories.LogsDir, real.Folder);
        Assert.Equal(platform.Directories.LogsDir, _started.Services.LogFolder);
        Assert.NotNull(real.CurrentFile);
        using var stream = new FileStream(real.CurrentFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Contains("started", new StreamReader(stream).ReadToEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_log_folder_that_cannot_be_made_does_not_stop_the_app_and_offers_no_folder()
    {
        var platform = HostPlatform();
        Assert.StartsWith(_root, platform.Directories.LogsDir, StringComparison.Ordinal);
        Directory.CreateDirectory(Path.GetDirectoryName(platform.Directories.LogsDir)!);
        File.WriteAllText(platform.Directories.LogsDir, "a file where the folder should be");

        _started = AppBootstrap.Start(options => Over(options, platform));

        Assert.True(_started.Services.BackendStarted);
        Assert.IsNotType<SerilogDiagnosticLog>(_started.Log);
        Assert.Null(_started.Services.LogFolder);
    }
}
