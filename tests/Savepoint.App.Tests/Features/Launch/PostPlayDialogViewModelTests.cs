using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
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
        Assert.Equal("3 files in your saves folder are new or changed since you started.", dialog.Body);
        Assert.Equal(580, dialog.DialogWidth);
    }

    [Fact]
    public async Task A_session_whose_program_was_never_seen_does_not_claim_any_play_time_and_points_at_the_process_name()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.NamedProcessNeverAppeared };

        var dialog = Build(exited);

        Assert.Equal("SavePoint didn't see the game running", dialog.Title);
        Assert.True(dialog.HasNotSeenHint);
        Assert.Equal("The program name from Edit game never showed up as running in two minutes, so SavePoint can't tell how long you played. The name may be wrong, or the game started slowly.", dialog.NotSeenHint);
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
        Assert.Equal("The program name in Edit game never showed up as running, so SavePoint went by when the program it started closed. If that's wrong, check the name.", dialog.NotSeenHint);
        Assert.Equal("Open Edit game", dialog.EditGameLabel);
    }

    [Fact]
    public async Task A_program_that_closed_right_away_with_an_error_says_so_with_its_code_and_does_not_blame_the_name()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.ProgramFailedAtStart, ProgramExitCode = 127 };

        var dialog = Build(exited);

        Assert.Equal("The program closed right away", dialog.Title);
        Assert.Equal("The program you chose closed right away with an error (code 127), so the game didn't start. If the game is in your Steam library, set it to start Through Steam in Edit game instead.", dialog.NotSeenHint);
        Assert.Equal("Open Edit game", dialog.EditGameLabel);
        Assert.DoesNotContain("name", dialog.NotSeenHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_process_list_that_could_not_be_read_does_not_blame_the_name_and_offers_no_editor()
    {
        var exited = (await _rig.ExitedAsync()) with { EndKind = SessionEndKind.ProcessListUnreadable };

        var dialog = Build(exited);

        Assert.Equal("SavePoint couldn't tell how long you played", dialog.Title);
        Assert.Equal("SavePoint couldn't check which programs were running, so it doesn't know when the game closed.", dialog.NotSeenHint);
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

        Assert.Equal("1 file in your saves folder is new or changed since you started.", Build(exited).Body);
        Assert.Equal("SavePoint found no new or changed saves to keep.", Build(exited with { NewFileCount = 0 }).Body);
    }

    [Fact]
    public async Task Large_file_counts_are_written_with_group_separators()
    {
        var exited = await _rig.ExitedAsync(newFiles: 200_000, installFiles: 12_000);

        var dialog = Build(exited);

        Assert.Equal("200,000 files in your saves folder are new or changed since you started.", dialog.Body);
        Assert.StartsWith("12,000 files in the game's own folder", dialog.InstallNote, StringComparison.Ordinal);
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
        Assert.Equal("Recommended. Your earlier snapshots stay as they are.", keep.Subtitle);
        Assert.Equal(Icons.AddCircle, keep.Icon);
        Assert.Equal("Replace \"Before the final boss\" with this session", update.Title);
        Assert.Equal("What \"Before the final boss\" held before moves to Recently removed.", update.Subtitle);
        Assert.Equal(Icons.Edit, update.Icon);
        Assert.Equal("Not now", skip.Title);
        Assert.Equal("Your saves stay in your game folders as new changes. SavePoint asks again before you switch profiles.", skip.Subtitle);
        Assert.Equal(Icons.Schedule, skip.Icon);
    }

    [Fact]
    public async Task A_session_that_changed_nothing_recommends_Not_now_and_does_not_recommend_keeping()
    {
        var played = await _rig.ExitedAsync(newFiles: 0);
        var dialog = Build(played with { NewFileCount = 0, ChangedInstallFileCount = 0, Options = played.Options with { Default = CompletionChoice.Skip } });

        Assert.Equal(CompletionChoice.Skip, dialog.SelectedChoice.Kind);
        Assert.Equal("Not now", dialog.PrimaryLabel);
        Assert.Equal("Your earlier snapshots stay as they are.", Card(dialog, CompletionChoice.CaptureNew).Subtitle);
        Assert.Equal("Recommended. Nothing changed, so there is nothing new to keep.", Card(dialog, CompletionChoice.Skip).Subtitle);
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
        Assert.Equal("Session - Jul 8, 2026", keep.Name);
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
        Assert.Equal("Recommended. \"Modded playthrough - 7-2-26\" and its newer snapshots stay as they are.", fork.Subtitle);
        Assert.Equal("Profile name:", fork.NameLabel);
        Assert.Equal("New progress - Jul 8, 2026", fork.Name);
        Assert.Equal("Continue \"Modded playthrough - 7-2-26\" from this session", move.Title);
        Assert.Equal("This session becomes the latest snapshot of \"Modded playthrough - 7-2-26\". 1 newer snapshot moves to Recently removed.", move.Subtitle);
        Assert.True(dialog.HasOlderNote);
        Assert.Equal("You played from an older snapshot of \"Modded playthrough - 7-2-26\", so this session can't go on the end of it.", dialog.OlderNote);
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
        Assert.Equal("14 files in the game's own folder also changed, as an update would.", dialog.InstallNote);
    }

    private async Task<PostPlayDialogViewModel> RunTimeOnlyAsync(int installFiles = 3, string rule = "**/*.dmp") =>
        new(await _rig.ExitedAsync(installFiles: installFiles, leaveOutRules: [rule]), _moddedName, _rig.Deps, gameId: SampleScenario.SkyrimId);

    private async Task<GameDefinition> SavedGameAsync() =>
        FakeAssert.Ok(await _rig.Backend.Games.ListAsync()).Single(game => game.Id == SampleScenario.SkyrimId);

    [Fact]
    public async Task Files_that_are_only_logs_or_crash_reports_are_said_to_be_that_and_not_an_update()
    {
        var dialog = await RunTimeOnlyAsync();

        Assert.True(dialog.HasInstallNote);
        Assert.Equal("3 files in the game's own folder changed. They're logs or crash reports, not an update.", dialog.InstallNote);
        Assert.DoesNotContain("update would", dialog.InstallNote, StringComparison.Ordinal);
        Assert.True(dialog.HasLeaveOut);
        Assert.Equal("Leave these out from now on", dialog.LeaveOutLabel);
    }

    [Fact]
    public async Task One_log_reads_in_the_singular()
    {
        var dialog = await RunTimeOnlyAsync(installFiles: 1);

        Assert.Equal("1 file in the game's own folder changed. It's a log or crash report, not an update.", dialog.InstallNote);
    }

    [Fact]
    public async Task A_change_to_the_game_itself_keeps_the_update_note_and_offers_nothing_to_leave_out()
    {
        var dialog = await NormalAsync(installFiles: 14);

        Assert.False(dialog.HasLeaveOut);
        Assert.Equal(string.Empty, dialog.LeaveOutLabel);
    }

    [Fact]
    public async Task Run_time_files_offer_nothing_when_the_dialog_does_not_know_the_game()
    {
        var dialog = new PostPlayDialogViewModel(await _rig.ExitedAsync(installFiles: 3, leaveOutRules: ["**/*.dmp"]), _moddedName, _rig.Deps);

        Assert.False(dialog.HasLeaveOut);
    }

    [Fact]
    public async Task Leaving_the_files_out_adds_the_rule_for_the_games_own_folder_only_and_leaves_the_sessions_choice_and_the_dialog_alone()
    {
        var dialog = await RunTimeOnlyAsync();
        dialog.SelectedChoice = Card(dialog, CompletionChoice.UpdateExisting);

        await dialog.LeaveOutCommand.ExecuteAsync(null);

        var game = await SavedGameAsync();
        Assert.Equal(["**/*.dmp"], game.InstallIgnorePaths);
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"], game.IgnorePaths);
        Assert.False(dialog.IsClosed);
        Assert.Equal(CompletionChoice.UpdateExisting, dialog.SelectedChoice.Kind);
        Assert.False(dialog.HasLeaveOut);
        Assert.True(dialog.HasLeaveOutResult);
        Assert.Equal("Done. You can change this under \"Files to leave out of the game's own folder\" in Edit game.", dialog.LeaveOutResult);
    }

    [Fact]
    public async Task A_rule_the_game_already_has_for_both_folders_is_not_added_again()
    {
        var dialog = await RunTimeOnlyAsync(rule: "**/*.log");

        await dialog.LeaveOutCommand.ExecuteAsync(null);

        var game = await SavedGameAsync();
        Assert.Empty(game.InstallIgnorePaths);
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"], game.IgnorePaths);
        Assert.False(dialog.HasLeaveOut);
    }

    [Fact]
    public async Task A_rule_the_game_already_has_for_its_own_folder_is_not_added_twice()
    {
        var dialog = await RunTimeOnlyAsync();
        var game = await SavedGameAsync();
        FakeAssert.Ok(await _rig.Backend.Games.UpdateAsync(game with { InstallIgnorePaths = ["**/*.dmp"] }));

        await dialog.LeaveOutCommand.ExecuteAsync(null);

        Assert.Equal(["**/*.dmp"], (await SavedGameAsync()).InstallIgnorePaths);
    }

    [Fact]
    public async Task When_the_game_cannot_be_changed_the_reason_shows_and_the_offer_stays()
    {
        var dialog = await RunTimeOnlyAsync();
        _rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.UpdateAsync), ErrorInfo.From(new InvalidOperationException("locked")));

        await dialog.LeaveOutCommand.ExecuteAsync(null);

        Assert.True(dialog.HasLeaveOut);
        Assert.True(dialog.HasLeaveOutResult);
        Assert.DoesNotContain("Done.", dialog.LeaveOutResult, StringComparison.Ordinal);
        Assert.Empty((await SavedGameAsync()).InstallIgnorePaths);
        Assert.False(dialog.IsClosed);
        Assert.Empty(_rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_rule_for_the_games_own_folder_is_added_without_looking_in_the_saves_folder()
    {
        var dialog = await RunTimeOnlyAsync(rule: "**/*.dmp");
        _rig.Backend.Games.SavesLeftOutByRule["**/*.dmp"] = ["crash.dmp"];

        await dialog.LeaveOutCommand.ExecuteAsync(null);

        // Even with a file in the saves folder that the rule would match: it never applies there.
        Assert.Equal(["**/*.dmp"], (await SavedGameAsync()).InstallIgnorePaths);
        Assert.Equal("Done. You can change this under \"Files to leave out of the game's own folder\" in Edit game.", dialog.LeaveOutResult);
    }

    [Fact]
    public async Task A_failure_that_comes_after_the_dialog_closed_is_shown_as_a_message_instead_of_being_lost()
    {
        var gate = new TaskCompletionSource();
        _rig.Backend.Games.ListGate = () => gate.Task;
        var dialog = await RunTimeOnlyAsync();
        var running = dialog.LeaveOutCommand.ExecuteAsync(null);
        dialog.ChooseNotNow();
        Assert.True(dialog.IsClosed);
        _rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.UpdateAsync), ErrorInfo.From(new InvalidOperationException("locked")));

        gate.SetResult();
        await running;

        var toast = Assert.Single(_rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.StartsWith("SavePoint couldn't leave those files out", toast.Text, StringComparison.Ordinal);
        Assert.Empty((await SavedGameAsync()).InstallIgnorePaths);
    }

    [Fact]
    public async Task The_main_button_names_the_selected_choice()
    {
        var dialog = await NormalAsync();
        Assert.Equal("Keep it", dialog.PrimaryLabel);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.UpdateExisting);
        Assert.Equal("Replace it", dialog.PrimaryLabel);

        dialog.SelectedChoice = Card(dialog, CompletionChoice.Skip);
        Assert.Equal("Not now", dialog.PrimaryLabel);
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
        Assert.Equal(new SessionCompletion.CaptureNew("Session - Jul 8, 2026"), await dialog.Result);
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
        Assert.Equal("\"Before the final boss\" will hold this session instead. What it held before is kept in Recently removed until you next free up space. Until then you can bring it back as a new profile. After that, it is deleted for good.", request.Message);
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
        Assert.Equal("1 newer snapshot is removed from \"Modded playthrough - 7-2-26\" and kept in Recently removed until you next free up space. Until then you can bring it back as a new profile. After that, it is deleted for good.", request.Message);
        Assert.Equal("Remove and continue", request.ConfirmLabel);
        Assert.True(request.Destructive);
        Assert.True(dialog.IsClosed);
        Assert.Equal(new SessionCompletion.MoveForward("Session - Jul 8, 2026"), await dialog.Result);
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
        Assert.Equal("This session becomes the latest snapshot of \"Modded playthrough - 7-2-26\". Nothing is removed.", dialog.Choices[1].Subtitle);
    }

    [Fact]
    public async Task Not_now_answers_with_a_skip_whichever_way_it_is_chosen()
    {
        var exited = await _rig.ExitedAsync();
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
