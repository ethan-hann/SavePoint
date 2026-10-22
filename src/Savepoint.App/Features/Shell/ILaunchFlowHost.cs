using System.ComponentModel;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

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

    /// <summary>
    /// The view model the Games screen shows below the profile table for a game: the "everything was already in place"
    /// banner (2a) or the running card (2c), or null when there is nothing to show. Its view is found by name.
    /// </summary>
    object? InlineContentFor(string gameId);

    /// <summary>Raised with the game id whenever <see cref="InlineContentFor"/> would return something different for that game. May be raised on any thread.</summary>
    event EventHandler<string>? InlineContentChanged;

    /// <summary>
    /// True for a save job whose outcome the flow reports itself, in a card on the Games screen. The job indicator then leaves a
    /// failure or half a save to that card instead of saying it a second time in a toast.
    /// </summary>
    /// <param name="jobId">The job whose end is being reported.</param>
    bool ShowsOutcomeOf(Guid jobId) => false;

    /// <summary>
    /// Raised with the settings as stored after the flow saved a preference (the launch dialog's "don't ask again").
    /// Whoever keeps a copy of the settings, the shell, takes it to stay current. May be raised on any thread.
    /// </summary>
    event EventHandler<AppSettings>? SettingsSaved;

    /// <summary>
    /// Raised with the game id when a failure is fixed by editing the game: the launch settings are wrong or a folder has
    /// moved. The Games screen opens the game editor for it; the flow never opens the editor itself. May be raised on any thread.
    /// </summary>
    event EventHandler<string>? EditGameRequested;
}
