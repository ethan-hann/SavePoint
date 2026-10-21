using Savepoint.App.Platform;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Errors;

namespace Savepoint.App.Hosting;

/// <summary>What starting the backend produced: the Api the app runs on, and the backend itself when it started.</summary>
/// <param name="Services">The Api for the view models. When the backend did not start this is an Api whose every call answers <paramref name="Failure"/>.</param>
/// <param name="Backend">The running backend; null when it did not start.</param>
/// <param name="Failure">Why the backend did not start; null when it did.</param>
public sealed record BootstrapResult(AppServices Services, CoreBackend? Backend, ErrorInfo? Failure);

/// <summary>Builds the real backend for the running machine (D18): a failure becomes a startup screen, never a crash.</summary>
public static class AppBootstrap
{
    /// <summary>Starts the backend over the machine's own folders and the bundled git.</summary>
    /// <param name="configure">Replaces parts of the backend, for tests; the defaults when null.</param>
    public static BootstrapResult Start(Func<BackendOptions, BackendOptions>? configure = null)
    {
        try
        {
            var options = new BackendOptions();
            var backend = CoreBackend.Create(configure is null ? options : configure(options));
            return new BootstrapResult(ServicesOf(backend), backend, null);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or SavepointException)
        {
            // No home folder, a configuration folder that cannot be written: the startup screen names it and offers to try again.
            return new BootstrapResult(Unavailable(ErrorInfo.From(e)), null, ErrorInfo.From(e));
        }
    }

    /// <summary>The Api of a running backend, with the platform's file manager for revealing files.</summary>
    /// <param name="backend">The running backend.</param>
    public static AppServices ServicesOf(CoreBackend backend) =>
        new(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus)
        {
            FileRevealer = new FileManagerRevealer(FileManager.CreateForCurrentOs()),
        };

    /// <summary>An Api whose every call fails with <paramref name="error"/>; only the startup screen reads it.</summary>
    /// <param name="error">Why the backend could not start.</param>
    public static AppServices Unavailable(ErrorInfo error) =>
        new(
            UnavailableApi.Create<IGamesApi>(error),
            UnavailableApi.Create<IProfilesApi>(error),
            UnavailableApi.Create<ILaunchApi>(error),
            UnavailableApi.Create<ISettingsApi>(error),
            UnavailableApi.Create<IHistoryApi>(error),
            UnavailableApi.Create<IJobsApi>(error),
            UnavailableApi.Create<IAppStatusApi>(error));
}
