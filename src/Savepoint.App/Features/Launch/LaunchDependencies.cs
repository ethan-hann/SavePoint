using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>Everything the launch flow and its dialogs are built from; the composition root assembles it and tests build it against the fake backend.</summary>
/// <param name="Launch">The launch Api: previews, start, discard, finish, complete.</param>
/// <param name="Profiles">The profiles Api, for saving unsaved changes and keeping them as a new profile.</param>
/// <param name="Settings">The settings Api, for reading the two launch preferences.</param>
/// <param name="SettingsWriter">Saves the "don't ask again" preference; the same writer every other feature saves settings through.</param>
/// <param name="Games">The games Api, for names.</param>
/// <param name="Jobs">The jobs Api, to cancel a switch and follow captures.</param>
/// <param name="Dialogs">Dialogs and confirmations.</param>
/// <param name="Text">Formats every message.</param>
/// <param name="Toasts">Reports failures that have no card of their own.</param>
/// <param name="Dispatcher">Brings Api events to the UI thread.</param>
/// <param name="Clock">Says what time it is and times the running card's refresh.</param>
/// <param name="Awaiter">Lets a dialog wait for a job it started.</param>
public sealed record LaunchDependencies(
    ILaunchApi Launch,
    IProfilesApi Profiles,
    ISettingsApi Settings,
    SettingsWriter SettingsWriter,
    IGamesApi Games,
    IJobsApi Jobs,
    IDialogs Dialogs,
    UiFormatter Text,
    ToastHostViewModel Toasts,
    IUiDispatcher Dispatcher,
    TimeProvider Clock,
    JobAwaiter Awaiter);
