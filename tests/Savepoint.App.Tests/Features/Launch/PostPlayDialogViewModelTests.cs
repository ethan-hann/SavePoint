using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Launch;

namespace Savepoint.App.Tests.Features.Launch;

public class PostPlayDialogViewModelTests : IDisposable
{
    private const string _moddedName = "Modded playthrough - 7-2-26";

    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private async Task<PostPlayDialogViewModel> NormalAsync(int newFiles = SampleScenario.NewSaveFiles, int installFiles = 0, LaunchTarget? target = null) =>
        new(await _rig.ExitedAsync(target ?? LaunchRig.ModdedTip, newFiles: newFiles, installFiles: installFiles), _moddedName, _rig.Deps);

    private async Task<PostPlayDialogViewModel> OlderAsync() =>
        new(await _rig.ExitedAsync(LaunchRig.ClearedBlackreachPoint), _moddedName, _rig.Deps);

    private PostPlayDialogViewModel Build(SessionState.Exited exited) => new(exited, _moddedName, _rig.Deps);

    private static PostPlayChoiceViewModel Card(PostPlayDialogViewModel dialog, CompletionChoice kind) =>
        dialog.Choices.Single(choice => choice.Kind == kind);

    [Fact]
    public async Task The_title_states_the_session_length_and_the_body_the_new_files()
    {
        var dialog = await NormalAsync();

        Assert.Equal("You played for 2 hours 14 minutes", dialog.Title);
        Assert.Equal("3 files in your saves folder are new or changed since you started. What should SavePoint do with them?", dialog.Body);
        Assert.Equal(580, dialog.DialogWidth);
    }

    [Fact]
    public async Task A_session_whose_program_was_never_seen_does_not_claim_any_play_time_and_points_at_the_process_name()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.NamedProcessNeverAppeared };

        var dialog = Build(exited);

        Assert.Equal("SavePoint didn't see the game running", dialog.Title);
        Assert.True(dialog.HasNotSeenHint);
        Assert.Equal("SavePoint waited two minutes for the game's program name from Edit game to show up as a running program, and it never did, so it can't tell how long you played. If the game did run, that name may be wrong, or the game may have taken longer than two minutes to start. Your saves stay as they are until you choose what to do with them.", dialog.NotSeenHint);
        Assert.Equal("Open Edit game", dialog.EditGameLabel);
        Assert.Equal($"{dialog.Body} {dialog.NotSeenHint}", dialog.Description);
        Assert.DoesNotContain("process", dialog.NotSeenHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_program_that_ran_but_whose_name_never_matched_keeps_its_play_time_and_softly_points_at_the_program_name()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.NamedProcessNeverMatched };

        var dialog = Build(exited);

        Assert.Equal("You played for 2 hours 14 minutes", dialog.Title);
        Assert.Equal("The game's program name in Edit game never showed up as a running program, so SavePoint went by when the program it started closed. If that isn't when you stopped playing, check the name.", dialog.NotSeenHint);
        Assert.Equal("Open Edit game", dialog.EditGameLabel);
    }

    [Fact]
    public async Task A_process_list_that_could_not_be_read_does_not_blame_the_name_and_offers_no_editor()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.ProcessListUnreadable };

        var dialog = Build(exited);

        Assert.Equal("SavePoint couldn't tell how long you played", dialog.Title);
        Assert.Equal("SavePoint couldn't check which programs were running on this computer, so it doesn't know when the game closed. Your saves stay as they are until you choose what to do with them.", dialog.NotSeenHint);
        Assert.False(dialog.HasEditGame);
    }

    [Fact]
    public async Task A_session_that_ran_shows_its_length_and_no_process_name_hint()
    {
        var dialog = await NormalAsync();

        Assert.False(dialog.HasNotSeenHint);
        Assert.Equal(string.Empty, dialog.NotSeenHint);
    }

    [Fact]
    public async Task Opening_Edit_game_from_the_hint_leaves_the_saves_alone_and_asks_for_the_editor()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.NamedProcessNeverAppeared };
        var opened = 0;
        var dialog = new PostPlayDialogViewModel(exited, _moddedName, _rig.Deps, editGame: () => opened++);

        dialog.EditGameCommand.Execute(null);

        Assert.Equal(1, opened);
        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.Skip(), await dialog.Result);
    }

    [Fact]
    public async Task One_new_file_and_no_new_files_read_naturally()
    {
        var exited = await _rig.ExitedAsync(newFiles: 1);

        Assert.Equal("1 file in your saves folder is new or changed since you started. What should SavePoint do with it?", Build(exited).Body);
        Assert.Equal("SavePoint found no new or changed saves to keep. What should it do with this session?", Build(exited with { NewFileCount = 0 }).Body);
    }

    [Fact]
    public async Task A_short_session_is_titled_in_minutes()
    {
        var exited = await _rig.ExitedAsync();
        var shorter = exited with { PlayDuration = TimeSpan.FromMinutes(12) };

        var dialog = new PostPlayDialogViewModel(shorter, _moddedName, _rig.Deps);

        Assert.Equal("You played for 12 minutes", dialog.Title);
    }

    [Fact]
    public async Task A_normal_session_offers_a_new_point_then_updating_the_latest_save_then_not_now_in_that_order()
    {
        var dialog = await NormalAsync();

        Assert.Equal([CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip], dialog.Choices.Select(c => c.Kind));
        var (keep, update, skip) = (dialog.Choices[0], dialog.Choices[1], dialog.Choices[2]);
        Assert.Equal("Keep this session as a new snapshot", keep.Title);
        Assert.Equal("Recommended. Your earlier snapshots stay exactly as they are.", keep.Subtitle);
        Assert.Equal(Icons.AddCircle, keep.Icon);
        Assert.Equal("Replace \"Before the final boss\" with this session", update.Title);
        Assert.Equal("What \"Before the final boss\" held before is removed. It stays in History under \"Recently removed\" until you next free up space, and you can bring it back as a new profile.", update.Subtitle);
        Assert.Equal(Icons.Edit, update.Icon);
        Assert.Equal("Not now", skip.Title);
        Assert.Equal("Nothing is lost. Your saves stay in your game folders as new changes, and SavePoint asks again before you switch profiles.", skip.Subtitle);
        Assert.Equal(Icons.Schedule, skip.Icon);
    }

    [Fact]
    public async Task The_service_default_is_selected_and_only_a_selected_card_with_a_field_shows_it()
    {
        var dialog = await NormalAsync();

        Assert.Same(Card(dialog, CompletionChoice.CaptureNew), dialog.SelectedChoice);
        Assert.True(Card(dialog, CompletionChoice.CaptureNew).IsSelected);
        Assert.True(Card(dialog, CompletionChoice.CaptureNew).ShowNameField);
        Assert.False(Card(dialog, CompletionChoice.UpdateExisting).IsSelected);
        Assert.False(Card(dialog, CompletionChoice.UpdateExisting).HasNameField);
        Assert.False(Card(dialog, CompletionChoice.Skip).ShowNameField);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.Skip);

        Assert.False(Card(dialog, CompletionChoice.CaptureNew).IsSelected);
        Assert.False(Card(dialog, CompletionChoice.CaptureNew).ShowNameField);
        Assert.True(Card(dialog, CompletionChoice.Skip).IsSelected);
    }

    [Fact]
    public async Task The_new_point_starts_with_a_dated_name_in_a_labelled_field()
    {
        var keep = Card(await NormalAsync(), CompletionChoice.CaptureNew);

        Assert.Equal("Name it:", keep.NameLabel);
        Assert.Equal("Session - 7-8-26", keep.Name);
    }

    [Fact]
    public async Task A_session_with_no_save_to_update_leaves_that_card_out()
    {
        var dialog = await NormalAsync(target: LaunchRig.VanillaTip);

        Assert.Equal([CompletionChoice.CaptureNew, CompletionChoice.Skip], dialog.Choices.Select(c => c.Kind));
    }

    [Fact]
    public async Task A_session_that_started_from_an_older_point_offers_a_new_profile_then_continuing_then_not_now()
    {
        var dialog = await OlderAsync();

        Assert.Equal([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], dialog.Choices.Select(c => c.Kind));
        Assert.Same(Card(dialog, CompletionChoice.ForkNewProfile), dialog.SelectedChoice);
        var (fork, move) = (dialog.Choices[0], dialog.Choices[1]);
        Assert.Equal("Keep this session as a new profile", fork.Title);
        Assert.Equal("Recommended. \"Modded playthrough - 7-2-26\" stays exactly as it is, newer snapshots included, and this session starts a profile of its own.", fork.Subtitle);
        Assert.Equal("Profile name:", fork.NameLabel);
        Assert.Equal("New progress - 7-8-26", fork.Name);
        Assert.Equal("Continue \"Modded playthrough - 7-2-26\" from this session", move.Title);
        Assert.Equal("This session becomes the latest snapshot of \"Modded playthrough - 7-2-26\". Its 1 newer snapshot is removed from it. It stays in History under \"Recently removed\" until you next free up space, and you can bring it back as a new profile.", move.Subtitle);
        Assert.True(dialog.HasOlderNote);
        Assert.Equal("You played from an older snapshot of \"Modded playthrough - 7-2-26\", so this session can't simply go on the end of it.", dialog.OlderNote);
    }

    [Fact]
    public async Task A_screen_reader_hears_why_the_choices_differ_when_a_session_started_from_an_older_snapshot_opens_the_dialog()
    {
        var dialog = await OlderAsync();

        Assert.Equal($"{dialog.Body} {dialog.OlderNote}", dialog.Description);
    }

    [Fact]
    public async Task A_normal_sessions_description_is_the_question_alone()
    {
        var dialog = await NormalAsync();

        Assert.Equal(dialog.Body, dialog.Description);
    }

    [Fact]
    public async Task A_normal_session_has_no_older_note_and_an_unchanged_install_has_no_install_note()
    {
        var dialog = await NormalAsync();

        Assert.False(dialog.HasOlderNote);
        Assert.False(dialog.HasInstallNote);
    }

    [Fact]
    public async Task Changes_to_the_games_own_files_while_playing_are_mentioned()
    {
        var dialog = await NormalAsync(installFiles: 14);

        Assert.True(dialog.HasInstallNote);
        Assert.Equal("14 files in the game's own folder also changed while you played, as an update would.", dialog.InstallNote);
    }

    [Fact]
    public async Task The_main_button_names_the_selected_choice_and_not_now_hides_when_it_would_repeat_it()
    {
        var dialog = await NormalAsync();
        Assert.Equal("Keep it", dialog.PrimaryLabel);
        Assert.True(dialog.ShowNotNowButton);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.UpdateExisting);
        Assert.Equal("Replace it", dialog.PrimaryLabel);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.Skip);
        Assert.Equal("Not now", dialog.PrimaryLabel);
        Assert.False(dialog.ShowNotNowButton);
    }

    [Fact]
    public async Task The_older_variants_name_their_main_button_too()
    {
        var dialog = await OlderAsync();
        Assert.Equal("Keep it", dialog.PrimaryLabel);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.MoveForward);
        Assert.Equal("Continue", dialog.PrimaryLabel);
    }

    [Fact]
    public async Task Save_it_with_the_default_choice_answers_with_a_new_point_carrying_the_typed_name()
    {
        var dialog = await NormalAsync();
        Card(dialog, CompletionChoice.CaptureNew).Name = "  After the Blackreach run  ";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.CaptureNew("After the Blackreach run"), await dialog.Result);
    }

    [Fact]
    public async Task A_blank_name_falls_back_to_the_suggested_one()
    {
        var dialog = await NormalAsync();
        Card(dialog, CompletionChoice.CaptureNew).Name = "   ";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.CaptureNew("Session - 7-8-26"), await dialog.Result);
    }

    [Fact]
    public async Task Updating_the_latest_save_asks_for_a_second_confirmation_first()
    {
        var dialog = await NormalAsync();
        dialog.SelectedChoice = Card(dialog, CompletionChoice.UpdateExisting);
        _rig.Dialogs.AnswerConfirm(false);

        await dialog.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_rig.Dialogs.Confirmations);
        Assert.Equal("Replace this snapshot?", request.Title);
        Assert.Equal("\"Before the final boss\" will hold this session instead. What it held before stays in History under \"Recently removed\" until you next free up space, and you can bring it back as a new profile. After that, it is deleted for good.", request.Message);
        Assert.Equal("Replace it", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public async Task Confirming_the_update_answers_with_an_update_that_keeps_the_points_name()
    {
        var dialog = await NormalAsync();
        dialog.SelectedChoice = Card(dialog, CompletionChoice.UpdateExisting);
        _rig.Dialogs.AnswerConfirm(true);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.UpdateExisting("Before the final boss"), await dialog.Result);
    }

    [Fact]
    public async Task A_new_profile_answers_with_the_name_for_both_the_profile_and_its_first_point()
    {
        var dialog = await OlderAsync();
        Card(dialog, CompletionChoice.ForkNewProfile).Name = "Blackreach detour";

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.ForkNewProfile("Blackreach detour", "Blackreach detour"), await dialog.Result);
    }

    [Fact]
    public async Task Continuing_the_profile_names_how_many_newer_points_leave_it_before_going_ahead()
    {
        var dialog = await OlderAsync();
        dialog.SelectedChoice = Card(dialog, CompletionChoice.MoveForward);
        _rig.Dialogs.AnswerConfirm(false, true);

        await dialog.SaveCommand.ExecuteAsync(null);
        Assert.False(dialog.IsClosed);

        await dialog.SaveCommand.ExecuteAsync(null);

        var request = _rig.Dialogs.Confirmations[0];
        Assert.Equal("Continue \"Modded playthrough - 7-2-26\" from this session?", request.Title);
        Assert.Equal("1 newer snapshot is removed from \"Modded playthrough - 7-2-26\". It stays in History under \"Recently removed\" until you next free up space, and you can bring it back as a new profile until then. After that, it is deleted for good.", request.Message);
        Assert.Equal("Remove and continue", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.MoveForward("Session - 7-8-26"), await dialog.Result);
    }

    [Fact]
    public async Task Several_newer_points_are_counted_in_the_confirmation()
    {
        var exited = await _rig.ExitedAsync(LaunchRig.ClearedBlackreachPoint);
        var options = exited.Options with { NewerPointCount = 3 };
        var dialog = new PostPlayDialogViewModel(exited with { Options = options }, _moddedName, _rig.Deps);
        dialog.SelectedChoice = Card(dialog, CompletionChoice.MoveForward);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.StartsWith("3 newer snapshots are removed from", _rig.Dialogs.Confirmations[0].Message);
        Assert.EndsWith("they are deleted for good.", _rig.Dialogs.Confirmations[0].Message);
    }

    [Fact]
    public async Task Continuing_with_nothing_newer_to_lose_needs_no_confirmation()
    {
        var exited = await _rig.ExitedAsync(LaunchRig.ClearedBlackreachPoint);
        var options = exited.Options with { NewerPointCount = 0 };
        var dialog = new PostPlayDialogViewModel(exited with { Options = options }, _moddedName, _rig.Deps);
        dialog.SelectedChoice = Card(dialog, CompletionChoice.MoveForward);

        await dialog.SaveCommand.ExecuteAsync(null);

        Assert.Empty(_rig.Dialogs.Confirmations);
        Assert.True(dialog.IsClosed);
        Assert.Equal("This session becomes the latest snapshot of \"Modded playthrough - 7-2-26\". Nothing is removed from it.", dialog.Choices[1].Subtitle);
    }

    [Fact]
    public async Task Not_now_answers_with_a_skip_whichever_way_it_is_chosen()
    {
        var exited = await _rig.ExitedAsync();
        var viaButton = Build(exited);
        viaButton.NotNowCommand.Execute(null);
        Assert.True(viaButton.IsClosed);
        Assert.Equal(new SessionCompletion.Skip(), await viaButton.Result);

        var viaCard = Build(exited);
        viaCard.SelectedChoice = Card(viaCard, CompletionChoice.Skip);
        await viaCard.SaveCommand.ExecuteAsync(null);
        Assert.True(viaCard.IsClosed);
        Assert.Equal(new SessionCompletion.Skip(), await viaCard.Result);

        var viaEscape = Build(exited);
        Assert.True(viaEscape.CanDismiss);
        viaEscape.RequestDismiss();
        Assert.True(viaEscape.IsClosed);
        Assert.Equal(new SessionCompletion.Skip(), await viaEscape.Result);
    }
}
