using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>Everything the shell view model is built from; <c>AppComposition</c> assembles it and tests build it against the fake backend.</summary>
/// <param name="Games">The games Api.</param>
/// <param name="Profiles">The profiles Api, for the per-game profile counts.</param>
/// <param name="Settings">The settings Api: layout, theme and the storage summary the shelf's sizes come from.</param>
/// <param name="Jobs">The jobs Api, for the status pill and for reloading after a job finishes.</param>
/// <param name="Text">Formats every message the shell shows.</param>
/// <param name="Dispatcher">Brings Api events back to the UI thread.</param>
/// <param name="Dialogs">Dialogs and pickers.</param>
/// <param name="Toasts">Where job results and failures are announced.</param>
/// <param name="Theme">Applies the saved theme and accent.</param>
/// <param name="Screens">One factory per destination; a destination without one shows a placeholder.</param>
/// <param name="LaunchFlow">The launch flow, or null while none exists.</param>
public sealed record ShellDependencies(
    IGamesApi Games,
    IProfilesApi Profiles,
    ISettingsApi Settings,
    IJobsApi Jobs,
    UiFormatter Text,
    IUiDispatcher Dispatcher,
    IDialogs Dialogs,
    ToastHostViewModel Toasts,
    IThemeApplier Theme,
    IReadOnlyList<IScreenFactory> Screens,
    ILaunchFlowHost? LaunchFlow = null);
