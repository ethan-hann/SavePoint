using System.ComponentModel;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// Where the launch flow plugs into the shell. The flow's state lives here, above every screen, so changing screens,
/// layouts or the selected game never loses a running session or its post-play prompt (D13.19). The shell exposes
/// whatever implementation it is given through <see cref="IShellContext.LaunchFlow"/>; it never creates one itself.
/// </summary>
public interface ILaunchFlowHost : INotifyPropertyChanged
{
    /// <summary>
    /// Starts the flow for a game: Play on a profile row, or "Go back to this" on a history point. The flow opens its own
    /// dialogs through <see cref="Common.Dialogs.IDialogs"/> and reports through its own properties.
    /// </summary>
    Task BeginAsync(string gameId, LaunchTarget target);

    /// <summary>True while the flow owns a session for the game (preparing, switching, running or waiting on the post-play choice); Play stays unavailable then.</summary>
    bool IsActive(string gameId);
}
