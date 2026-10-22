using Savepoint.App.Features.Common.Dialogs;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Features.Games;

public sealed partial class GamesScreenViewModel
{
    async Task IProfileRowActions.PlayAsync(ProfileRowViewModel row)
    {
        // A command's Execute runs even when CanExecute is false, so the gate is checked again here.
        if (!row.CanPlay)
        {
            return;
        }

        await _shell.LaunchFlow.BeginAsync(row.GameId, new LaunchTarget.ProfileTip(row.Id));
    }

    async Task IProfileRowActions.EditAsync(ProfileRowViewModel row)
    {
        var dialog = new EditProfileDialogViewModel(
            row.Name,
            row.Notes,
            row.VersionOverride,
            row.DetectedVersion,
            changes => _deps.Profiles.UpdateAsync(row.GameId, row.Id, changes),
            _deps.Text);

        if (await _deps.Dialogs.ShowAsync(dialog))
        {
            await RefreshAfterChangeAsync();
        }
    }

    // A snapshot of what the folders hold now, added to this profile; the job's result and any failure reach the player through the shell's job status.
    // A profile with no snapshot yet starts with it, so the player is told first that it is whatever the folders hold, which may be another profile's files.
    async Task IProfileRowActions.TakeSnapshotAsync(ProfileRowViewModel row)
    {
        // A command's Execute runs even when CanExecute is false, so the gate is checked again here.
        if (!row.CanPlay || !row.CanOfferSnapshot)
        {
            return;
        }

        if (row.SnapshotNow == SnapshotNowAvailability.FirstSnapshot
            && !await _deps.Dialogs.ConfirmAsync(new(
                _deps.Text.Message("games.profileTable.takeSnapshot.firstConfirm.title", ("name", row.Name)),
                _deps.Text.Message("games.profileTable.takeSnapshot.firstConfirm.message", ("name", row.Name)),
                _deps.Text.Message("games.profileTable.takeSnapshot.firstConfirm.confirm"))))
        {
            return;
        }

        var name = _deps.Text.Message("games.profileTable.takeSnapshot.defaultName", ("date", _deps.Text.NameDate(_deps.Clock.GetUtcNow())));
        var started = await _deps.Profiles.CaptureAsync(row.GameId, row.Id, name);
        if (!started.Ok)
        {
            ShowError(started.Error);
        }
    }

    async Task IProfileRowActions.DuplicateAsync(ProfileRowViewModel row)
    {
        var dialog = new DuplicateProfileDialogViewModel(row.Name, name => _deps.Profiles.DuplicateAsync(row.GameId, row.Id, name), _deps.Text);

        if (await _deps.Dialogs.ShowAsync(dialog))
        {
            await RefreshAfterChangeAsync();
        }
    }

    // The preview decides what is asked: a refusal is shown as it is, a plain confirmation when nothing is lost with the profile, a warning when some history is (D17).
    async Task IProfileRowActions.DeleteAsync(ProfileRowViewModel row)
    {
        var preview = await _deps.Profiles.GetDeletePreviewAsync(row.GameId, row.Id);
        if (!preview.Ok)
        {
            ShowError(preview.Error);
            return;
        }

        if (preview.Value.BlockedBy is { } blocked)
        {
            ShowError(blocked);
            return;
        }

        // Snapshots a recently removed record keeps are not lost: they go back to that list, and the question says so apart from the ones that are.
        var points = preview.Value.PointsOnlyHere;
        var back = preview.Value.PointsBackToRemoved;
        var message = (points > 0, back > 0) switch
        {
            (true, true) => _deps.Text.Message("games.profileTable.deleteConfirm.messageWithPointsAndRemoved", ("count", points), ("back", back)),
            (true, false) => _deps.Text.Message("games.profileTable.deleteConfirm.messageWithPoints", ("count", points)),
            (false, true) => _deps.Text.Message("games.profileTable.deleteConfirm.messageOnlyRemoved", ("count", back)),
            _ => _deps.Text.Message("games.profileTable.deleteConfirm.message", ("name", row.Name)),
        };
        var confirmed = await _deps.Dialogs.ConfirmAsync(new(
            _deps.Text.Message("games.profileTable.deleteConfirm.title", ("name", row.Name)),
            message,
            _deps.Text.Message("games.profileTable.deleteConfirm.confirm"),
            Destructive: true));
        if (!confirmed)
        {
            return;
        }

        var deleted = await _deps.Profiles.DeleteAsync(row.GameId, row.Id);
        if (!deleted.Ok)
        {
            ShowError(deleted.Error);
            return;
        }

        await RefreshAfterChangeAsync();
    }

    // The wizard follows its own save, so closing it mid-save is fine; the table is refreshed whenever it closes.
    private async Task NewProfileAsync()
    {
        if (_game is not { } game)
        {
            return;
        }

        var wizard = new NewProfileWizardViewModel(game, _deps.Games, _deps.Profiles, _deps.Jobs, _deps.Text, _deps.Dispatcher, _deps.Clock);
        var shown = _deps.Dialogs.ShowAsync(wizard);
        _ = wizard.LoadPreviewAsync();
        await shown;
        await RefreshAfterChangeAsync();
    }

    // Deleting a game asks for its name to be typed and is refused up front while its session is active (D13.5).
    private async Task DeleteGameAsync()
    {
        if (_game is not { } game)
        {
            return;
        }

        if (_shell.LaunchFlow.IsActive(game.Id))
        {
            ShowError(ErrorInfo.From(new GameRunningException(game.Id, game.Name)));
            return;
        }

        var confirmed = await _deps.Dialogs.ConfirmAsync(new(
            _deps.Text.Message("games.deleteConfirm.title", ("name", game.Name)),
            _deps.Text.Message("games.deleteConfirm.message", ("name", game.Name), ("count", Profiles.Count)),
            _deps.Text.Message("games.deleteConfirm.confirm"),
            Destructive: true,
            Typed: new TypedConfirmation(game.Name, _deps.Text.Message("games.deleteConfirm.prompt", ("name", game.Name)))));
        if (!confirmed)
        {
            return;
        }

        var deleted = await _deps.Games.DeleteAsync(game.Id);
        if (!deleted.Ok)
        {
            ShowError(deleted.Error);
            return;
        }

        await RefreshAfterChangeAsync();
    }

    // The shell reloads the library and tells this screen; reloading here as well means the table is current when the action ends.
    private async Task RefreshAfterChangeAsync()
    {
        await _shell.RefreshLibraryAsync();
        await ReloadAsync();
    }
}
