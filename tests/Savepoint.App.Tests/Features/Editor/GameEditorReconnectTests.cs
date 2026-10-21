using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Editor;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorReconnectTests
{
    private const string _install = @"C:\Games\Hollow Knight";
    private const string _oldGameId = "hollow-knight-old1";

    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static readonly OrphanedHistory _history = new(
        _oldGameId,
        [RepoRole.Install, RepoRole.External],
        [new RecoverableProfile("profile/modded-run-ab12", "Modded run"), new RecoverableProfile("profile/xy99", null)]);

    private static EditorRig RigWithHistory() =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithOrphanedHistory(_install, _history)));

    private static GameEditorViewModel Filled(EditorRig rig)
    {
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = _install;
        editor.ExePath = "hollow_knight.exe";
        return editor;
    }

    // Saves a new game, confirms its folders, and stops at the reconnect question.
    private static async Task<(Task Saving, ReconnectDialogViewModel Dialog)> SaveUntilReconnectAsked(EditorRig rig, GameEditorViewModel editor)
    {
        var saving = editor.SaveCommand.ExecuteAsync(null);
        Assert.IsType<ConsentDialogViewModel>(rig.Dialogs.Shown[0]).ConfirmCommand.Execute(null);
        var dialog = await EditorRig.Eventually(() => rig.Dialogs.Shown.OfType<ReconnectDialogViewModel>().FirstOrDefault());
        Assert.False(saving.IsCompleted, "The save should be waiting for the reconnect question.");
        return (saving, dialog);
    }

    [Fact]
    public void The_dialog_lists_each_recoverable_profile_and_falls_back_for_one_without_a_name()
    {
        var dialog = new ReconnectDialogViewModel(_history, _text);

        Assert.Equal("Found saved history for this location", dialog.Title);
        Assert.Equal(["Recovered: Modded run", "Recovered profile"], dialog.Lines);
        Assert.Equal(
            [new RecoveredProfile("profile/modded-run-ab12", "Modded run"), new RecoveredProfile("profile/xy99", "Recovered profile")],
            dialog.Recovered);
    }

    [Fact]
    public void Each_button_answers_with_its_own_choice_and_Cancel_and_Escape_answer_null()
    {
        var reconnect = new ReconnectDialogViewModel(_history, _text);
        var fresh = new ReconnectDialogViewModel(_history, _text);
        var cancel = new ReconnectDialogViewModel(_history, _text);
        var escape = new ReconnectDialogViewModel(_history, _text);

        reconnect.ReconnectCommand.Execute(null);
        fresh.StartFreshCommand.Execute(null);
        cancel.CancelCommand.Execute(null);
        escape.RequestDismiss();

        Assert.Equal(ReconnectChoice.Reconnect, Answer(reconnect));
        Assert.Equal(ReconnectChoice.StartFresh, Answer(fresh));
        Assert.Null(Answer(cancel));
        Assert.Null(Answer(escape));
    }

    private static ReconnectChoice? Answer(ReconnectDialogViewModel dialog)
    {
        Assert.True(dialog.Result.IsCompleted, "The dialog did not answer.");
        return dialog.Result.Result;
    }

    [Fact]
    public async Task Found_history_opens_the_reconnect_dialog_and_saves_nothing_yet()
    {
        var rig = RigWithHistory();
        var editor = Filled(rig);

        var (_, dialog) = await SaveUntilReconnectAsked(rig, editor);

        Assert.Equal(2, dialog.Recovered.Count);
        Assert.Empty(rig.Calls("ResolveOrphanAsync"));
        Assert.Empty(rig.Shell.Calls);
    }

    [Fact]
    public async Task Reconnecting_restores_the_earlier_profiles_under_the_old_game_and_closes_the_editor()
    {
        var rig = RigWithHistory();
        var editor = Filled(rig);
        var (saving, dialog) = await SaveUntilReconnectAsked(rig, editor);

        dialog.ReconnectCommand.Execute(null);
        await saving;

        var call = Assert.Single(rig.Calls("ResolveOrphanAsync"));
        var reconnect = Assert.IsType<OrphanResolution.Reconnect>(call.Args[1]);
        Assert.Equal(_oldGameId, reconnect.ExistingGameId);
        Assert.Equal(
            [new RecoveredProfile("profile/modded-run-ab12", "Modded run"), new RecoveredProfile("profile/xy99", "Recovered profile")],
            reconnect.Profiles);
        Assert.Equal(["refresh", $"select:{_oldGameId}", "close"], rig.Shell.Calls);
    }

    [Fact]
    public async Task Starting_fresh_adds_the_game_under_its_new_id()
    {
        var rig = RigWithHistory();
        var editor = Filled(rig);
        var (saving, dialog) = await SaveUntilReconnectAsked(rig, editor);

        dialog.StartFreshCommand.Execute(null);
        await saving;

        var call = Assert.Single(rig.Calls("ResolveOrphanAsync"));
        var definition = Assert.IsType<GameDefinition>(call.Args[0]);
        Assert.IsType<OrphanResolution.StartFresh>(call.Args[1]);
        Assert.Equal(["refresh", $"select:{definition.Id}", "close"], rig.Shell.Calls);
        Assert.NotEqual(_oldGameId, definition.Id);
    }

    [Fact]
    public async Task Cancelling_the_question_keeps_the_editor_open_and_the_next_save_asks_again()
    {
        var rig = RigWithHistory();
        var editor = Filled(rig);
        var (saving, dialog) = await SaveUntilReconnectAsked(rig, editor);

        dialog.CancelCommand.Execute(null);
        await saving;

        Assert.Empty(rig.Calls("ResolveOrphanAsync"));
        Assert.Empty(rig.Shell.Calls);
        Assert.False(editor.IsSaving);

        var again = editor.SaveCommand.ExecuteAsync(null);

        Assert.False(again.IsCompleted);
        Assert.Equal(2, rig.Dialogs.Shown.OfType<ReconnectDialogViewModel>().Count());
    }

    [Fact]
    public async Task A_failure_while_reconnecting_shows_a_card_and_keeps_the_editor_open()
    {
        var rig = RigWithHistory();
        var editor = Filled(rig);
        var (saving, dialog) = await SaveUntilReconnectAsked(rig, editor);
        rig.Backend.FailNext<IGamesApi>("ResolveOrphanAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "boom"));

        dialog.ReconnectCommand.Execute(null);
        await saving;

        Assert.NotNull(editor.FormError);
        Assert.Equal("boom", editor.FormError.Detail);
        Assert.Empty(rig.Shell.Calls);
    }

    [Fact]
    public async Task Editing_a_game_never_opens_the_reconnect_dialog_and_shows_the_error_instead()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.Name = "Skyrim SE";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new ReconnectableGameException(_oldGameId, ["install"], ["profile/a"])));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Empty(rig.Dialogs.Shown);
        Assert.NotNull(editor.FormError);
        Assert.Equal("Saved history was found here", editor.FormError.Title);
    }
}
