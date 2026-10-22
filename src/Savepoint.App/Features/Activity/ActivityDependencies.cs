using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Activity;

/// <summary>Everything the Activity screen is built from; the composition root assembles it and tests build it against the fake backend.</summary>
/// <param name="Jobs">The jobs Api: the running and recent jobs, their changes, and Cancel.</param>
/// <param name="Dialogs">Asks before a Cancel that would leave something half done.</param>
/// <param name="Text">Formats every message the screen shows.</param>
/// <param name="Dispatcher">Brings job changes back to the UI thread.</param>
/// <param name="Clock">What "now" means for the time a running job has left.</param>
public sealed record ActivityDependencies(IJobsApi Jobs, IDialogs Dialogs, UiFormatter Text, IUiDispatcher Dispatcher, TimeProvider Clock);
