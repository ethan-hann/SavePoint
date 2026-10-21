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
public sealed record AppServices(
    IGamesApi Games,
    IProfilesApi Profiles,
    ILaunchApi Launch,
    ISettingsApi Settings,
    IHistoryApi History,
    IJobsApi Jobs,
    IAppStatusApi AppStatus);
