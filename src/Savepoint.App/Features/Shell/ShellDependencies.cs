using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>Everything the shell view model is built from; <c>AppComposition</c> assembles it and tests build it against the fake backend.</summary>
/// <param name="Games">The games Api.</param>
/// <param name="Profiles">The profiles Api, for the per-game profile counts.</param>
/// <param name="Settings">The settings Api: reading the layout and theme, and the storage summary the shelf's sizes come from.</param>
/// <param name="SettingsWriter">Saves layout changes; the same writer every other feature saves settings through.</param>
/// <param name="Jobs">The jobs Api, for the status pill and for reloading after a job finishes.</param>
/// <param name="Text">Formats every message the shell shows.</param>
/// <param name="Dispatcher">Brings Api events back to the UI thread.</param>
/// <param name="Dialogs">Dialogs and pickers.</param>
/// <param name="Toasts">Where job results and failures are announced.</param>
/// <param name="Theme">Applies the saved theme and accent.</param>
/// <param name="Screens">One factory per destination; the shell refuses to start without one for every destination.</param>
/// <param name="Artwork">Each game's artwork for its tiles; a game with none keeps its name tint.</param>
/// <param name="LaunchFlow">The launch flow that owns starting a game.</param>
public sealed record ShellDependencies(
    IGamesApi Games,
    IProfilesApi Profiles,
    ISettingsApi Settings,
    SettingsWriter SettingsWriter,
    IJobsApi Jobs,
    UiFormatter Text,
    IUiDispatcher Dispatcher,
    IDialogs Dialogs,
    ToastHostViewModel Toasts,
    IThemeApplier Theme,
    IReadOnlyList<IScreenFactory> Screens,
    IArtworkProvider Artwork,
    ILaunchFlowHost LaunchFlow);
