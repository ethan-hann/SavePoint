using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Platform;

namespace Savepoint.App.Composition;

/// <summary>What a feature's composition method may use to build its view models.</summary>
/// <param name="Services">The Api.</param>
/// <param name="Dialogs">Dialogs and pickers.</param>
/// <param name="Text">Formats messages.</param>
/// <param name="Toasts">Shows transient messages.</param>
/// <param name="Dispatcher">Brings Api events to the UI thread.</param>
/// <param name="Time">What "now" means to the features; the system clock when null.</param>
/// <param name="Attention">Calls the player back when a session ends behind another window; does nothing when null.</param>
public sealed record FeatureContext(AppServices Services, IDialogs Dialogs, UiFormatter Text, ToastHostViewModel Toasts, IUiDispatcher Dispatcher, TimeProvider? Time = null, ISessionAttention? Attention = null)
{
    /// <summary>What "now" means to the features, and what their timers run on.</summary>
    public TimeProvider Clock => Time ?? TimeProvider.System;

    /// <summary>The one writer of settings, shared by every feature that changes them (see <see cref="SettingsWriter"/>).</summary>
    public SettingsWriter SettingsWriter { get; } = new(Services.Settings);
}
