using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;

namespace Savepoint.App.Composition;

/// <summary>What a feature's composition method may use to build its view models.</summary>
/// <param name="Services">The Api.</param>
/// <param name="Dialogs">Dialogs and pickers.</param>
/// <param name="Text">Formats messages.</param>
/// <param name="Toasts">Shows transient messages.</param>
/// <param name="Dispatcher">Brings Api events to the UI thread.</param>
public sealed record FeatureContext(AppServices Services, IDialogs Dialogs, UiFormatter Text, ToastHostViewModel Toasts, IUiDispatcher Dispatcher);
