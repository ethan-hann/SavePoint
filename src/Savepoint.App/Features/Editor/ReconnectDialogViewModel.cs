using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Editor;

/// <summary>What the user chose when saved history was found at a new game's folders.</summary>
public enum ReconnectChoice
{
    /// <summary>Bring the earlier profiles back.</summary>
    Reconnect,

    /// <summary>Leave the earlier history where it is and start the game with none.</summary>
    StartFresh,
}

/// <summary>
/// "This folder has snapshots from before": a game that is no longer in the library left history at the folders being
/// added. The dialog lists the profiles that can come back and answers with the choice, or null when the user backs out.
/// </summary>
public sealed partial class ReconnectDialogViewModel : DialogViewModel<ReconnectChoice?>
{
    /// <param name="history">What was found.</param>
    /// <param name="text">Formats the copy and supplies the name for a profile with no readable one.</param>
    public ReconnectDialogViewModel(OrphanedHistory history, UiFormatter text)
        : base(text.Message("gameEditor.reconnect.title"))
    {
        var fallback = text.Message("gameEditor.reconnect.genericProfileName");
        Recovered = [.. history.Profiles.Select(profile => new RecoveredProfile(profile.BranchName, profile.SuggestedName ?? fallback))];
        Lines = [.. history.Profiles.Select(profile => profile.SuggestedName is { } name ? text.Message("gameEditor.reconnect.recoveredProfileName", ("name", name)) : fallback)];
        Message = text.Message("gameEditor.reconnect.message");
        ReconnectLabel = text.Message("gameEditor.reconnect.reconnect");
        StartFreshLabel = text.Message("gameEditor.reconnect.startFresh");
        CancelLabel = text.Message("common.cancel");
    }

    /// <summary>The profiles that come back on reconnecting, with the names the user will see.</summary>
    public IReadOnlyList<RecoveredProfile> Recovered { get; }

    /// <summary>One line per recoverable profile, as the dialog lists them.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>What was found and what the two choices do.</summary>
    public string Message { get; }

    /// <summary>The reconnect button's text.</summary>
    public string ReconnectLabel { get; }

    /// <summary>The start-fresh button's text.</summary>
    public string StartFreshLabel { get; }

    /// <summary>The cancel button's text.</summary>
    public string CancelLabel { get; }

    /// <inheritdoc />
    public override string Description => Message;

    /// <inheritdoc />
    public override double DialogWidth => 480;

    /// <inheritdoc />
    protected override ReconnectChoice? DismissResult => null;

    [RelayCommand]
    private void Reconnect() => Close(ReconnectChoice.Reconnect);

    [RelayCommand]
    private void StartFresh() => Close(ReconnectChoice.StartFresh);

    [RelayCommand]
    private void Cancel() => Close(null);
}
