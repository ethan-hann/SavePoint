using Savepoint.App.Features.Settings;
using Savepoint.Core.Api;

namespace Savepoint.App;

/// <summary>
/// The Api the view models are built from. The composition root creates one from the real Core services; tests build
/// one from the fake backend. View models take the individual interfaces, never this record.
/// </summary>
/// <param name="Games">The games Api.</param>
/// <param name="Profiles">The profiles Api.</param>
/// <param name="Launch">The launch Api.</param>
/// <param name="Settings">The settings Api.</param>
/// <param name="History">The history Api.</param>
/// <param name="Jobs">The jobs Api.</param>
/// <param name="AppStatus">The startup status Api.</param>
/// <param name="Artwork">The artwork Api.</param>
public sealed record AppServices(
    IGamesApi Games,
    IProfilesApi Profiles,
    ILaunchApi Launch,
    ISettingsApi Settings,
    IHistoryApi History,
    IJobsApi Jobs,
    IAppStatusApi AppStatus,
    IArtworkApi Artwork)
{
    /// <summary>
    /// Shows a file in the operating system's file manager; the running app supplies the platform one. Null in tests, where
    /// the screens that need it bring their own.
    /// </summary>
    public IFileRevealer? FileRevealer { get; init; }

    /// <summary>
    /// False when the backend failed to start and every Api answers the same failure; the startup screen then asks the user to restart
    /// instead of offering a retry that cannot work.
    /// </summary>
    public bool BackendStarted { get; init; } = true;

    /// <summary>
    /// The folder the diagnostic log is written to (D34), for the screens that offer to open it; null when no log could be opened, or in a
    /// test that has none.
    /// </summary>
    public string? LogFolder { get; init; }
}
