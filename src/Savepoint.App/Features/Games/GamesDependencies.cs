using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Navigation;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Games;

/// <summary>Everything the Games screen is built from; the composition root assembles it and tests build it against the fake backend.</summary>
/// <param name="Games">The games Api, for the storage footer, deleting a game and the wizard's capture preview.</param>
/// <param name="Profiles">The profiles Api.</param>
/// <param name="Jobs">The jobs Api, which the New profile wizard follows while it saves.</param>
/// <param name="Dialogs">Dialogs and confirmations.</param>
/// <param name="Text">Formats every message the screen shows.</param>
/// <param name="Toasts">Where failures that have no dialog to sit in are announced.</param>
/// <param name="Dispatcher">Brings Api and launch-flow events back to the UI thread.</param>
/// <param name="Clock">What "now" means for the wizard's default profile name.</param>
/// <param name="Editor">Builds the game editor pane; null while there is none, which hides Edit game.</param>
public sealed record GamesDependencies(
    IGamesApi Games,
    IProfilesApi Profiles,
    IJobsApi Jobs,
    IDialogs Dialogs,
    UiFormatter Text,
    ToastHostViewModel Toasts,
    IUiDispatcher Dispatcher,
    TimeProvider Clock,
    IGameEditorFactory? Editor);
