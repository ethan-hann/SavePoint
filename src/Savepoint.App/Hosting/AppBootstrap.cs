using System.Runtime.InteropServices;
using Savepoint.App.Features.Settings;
using Savepoint.App.Platform;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Platform;

namespace Savepoint.App.Hosting;

/// <summary>What starting the backend produced: the Api the app runs on, and the backend itself when it started.</summary>
/// <param name="Services">The Api for the view models. When the backend did not start this is an Api whose every call answers <paramref name="Failure"/>, and <see cref="AppServices.BackendStarted"/> is false.</param>
/// <param name="Backend">The running backend; null when it did not start.</param>
/// <param name="Failure">Why the backend did not start; null when it did.</param>
/// <param name="Log">The diagnostic log the backend writes to, which the crash handler writes to as well. Dispose it when the app ends if it is <see cref="IDisposable"/>.</param>
public sealed record BootstrapResult(AppServices Services, CoreBackend? Backend, ErrorInfo? Failure, IDiagnosticLog Log);

/// <summary>Builds the real backend for the running machine (D18): a failure becomes a startup screen, never a crash.</summary>
public static class AppBootstrap
{
    /// <summary>Starts the backend over the machine's own folders and the bundled git, with the real launch orchestrator.</summary>
    /// <param name="configure">Replaces parts of the backend, for tests; the defaults when null.</param>
    /// <param name="log">The diagnostic log to use, for tests; when null the real log is opened in the platform's log folder (D34).</param>
    /// <param name="build">The build text the startup line carries, for tests; the running app's version and commit when null.</param>
    public static BootstrapResult Start(Func<BackendOptions, BackendOptions>? configure = null, IDiagnosticLog? log = null, string? build = null)
    {
        var diagnostics = log ?? DiagnosticLog.None;
        string? logFolder = null;
        try
        {
            var options = new BackendOptions { Launch = LaunchOrchestrator.Factory(), ArtworkHttp = ArtworkHttpHandler.CreateForApp() };
            if (configure is not null)
            {
                options = configure(options);
            }

            // The platform decides where the log goes, so it is resolved here once and the backend is given the same one.
            var platform = options.Platform ?? PlatformFactory.ForCurrentOs();
            if (log is null)
            {
                var opened = SerilogDiagnosticLog.TryCreate(platform.Directories.LogsDir);
                diagnostics = opened ?? DiagnosticLog.None;
                logFolder = opened?.Folder;
            }
            else
            {
                logFolder = platform.Directories.LogsDir;
            }

            diagnostics.Information($"SavePoint {build ?? SettingsEnvironment.ThisAppBuild()} started on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}.");
            var backend = CoreBackend.Create(options with { Platform = platform, Log = diagnostics });
            return new BootstrapResult(ServicesOf(backend, logFolder), backend, null, diagnostics);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // No home folder, a configuration folder that cannot be written: the startup screen names it and asks for a restart.
            // The log line comes first, so it is on disk before anything is shown.
            diagnostics.Error("SavePoint could not start its backend: " + e.Message, e);
            return new BootstrapResult(Unavailable(ErrorInfo.From(e), logFolder), null, ErrorInfo.From(e), diagnostics);
        }
    }

    /// <summary>The Api of a running backend, with the platform's file manager for revealing files.</summary>
    /// <param name="backend">The running backend.</param>
    /// <param name="logFolder">Where the diagnostic log is written, for the screens that offer to open it; null when there is no log folder.</param>
    public static AppServices ServicesOf(CoreBackend backend, string? logFolder = null) =>
        new(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork)
        {
            FileRevealer = new FileManagerRevealer(FileManager.CreateForCurrentOs()),
            LogFolder = logFolder,
        };

    /// <summary>An Api whose every call fails with <paramref name="error"/>; only the startup screen reads it.</summary>
    /// <param name="error">Why the backend could not start.</param>
    /// <param name="logFolder">Where the diagnostic log is written, for the startup screen to offer; null when there is no log folder.</param>
    public static AppServices Unavailable(ErrorInfo error, string? logFolder = null) =>
        new(
            UnavailableApi.Create<IGamesApi>(error),
            UnavailableApi.Create<IProfilesApi>(error),
            UnavailableApi.Create<ILaunchApi>(error),
            UnavailableApi.Create<ISettingsApi>(error),
            UnavailableApi.Create<IHistoryApi>(error),
            UnavailableApi.Create<IJobsApi>(error),
            UnavailableApi.Create<IAppStatusApi>(error),
            UnavailableApi.Create<IArtworkApi>(error))
        {
            BackendStarted = false,
            LogFolder = logFolder,
        };
}
