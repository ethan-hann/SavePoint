using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorModsFolderTests
{
    private static EditorRig RigWith(GameDefinition game) =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game)));

    private static FolderStatus Status(RepoRole role, string raw, string resolved, bool exists = true, bool confirmed = true) =>
        new(role, raw, resolved, exists, new ConsentStatus(confirmed, ScenarioBuilder.DefaultNow));

    private static (Task Saving, ConsentDialogViewModel Dialog) SaveUntilAsked(EditorRig rig, GameEditorViewModel editor)
    {
        var shownBefore = rig.Dialogs.Shown.Count;
        var saving = editor.SaveCommand.ExecuteAsync(null);
        Assert.False(saving.IsCompleted, "The save should be waiting for the confirmation.");
        return (saving, Assert.IsType<ConsentDialogViewModel>(rig.Dialogs.Shown[shownBefore]));
    }

    [Fact]
    public async Task A_game_with_a_mods_folder_opens_with_it_in_the_mods_field_and_saves_it_back_unchanged()
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.Name = "7 Days";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ModsSamples.ModsRaw, editor.ModsPath);
        Assert.Equal(new ModsLocation(ModsSamples.ModsRaw), rig.Saved("UpdateAsync").Mods);
    }

    [Fact]
    public async Task Clearing_the_mods_field_saves_the_game_without_a_mods_folder()
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();

        editor.ModsPath = "  ";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Null(rig.Saved("UpdateAsync").Mods);
    }

    [Fact]
    public void Changing_the_mods_field_marks_the_form_as_changed()
    {
        var editor = RigWith(ModsSamples.SevenDays).Edit(ModsSamples.Id);
        Assert.False(editor.IsDirty);

        editor.ModsPath = @"{appData}\Elsewhere";

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void The_mods_field_has_a_question_button_whose_answer_says_most_games_keep_mods_inside_the_game_folder()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Where do my mods live?", editor.ModsHelp.Question);
        Assert.False(editor.ModsHelp.IsOpen);
        Assert.StartsWith("Most games keep mods inside the game folder, and SavePoint already snapshots those.", editor.ModsHelp.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_game_with_a_mods_folder_confirms_the_game_mods_and_saves_folders_in_the_order_they_are_shown()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "7 Days to Die";
        editor.InstallPath = ModsSamples.InstallPath;
        editor.ModsPath = ModsSamples.ModsRaw;
        editor.SavesPath = ModsSamples.SavesRaw;
        editor.ExePath = "7DaysToDie.exe";

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal(
            [
                new ConsentFolder("Game install folder", ModsSamples.InstallPath),
                new ConsentFolder("Mods folder", ModsSamples.ModsResolved),
                new ConsentFolder("Saves folder", ModsSamples.SavesResolved),
            ],
            dialog.Folders);
        Assert.Equal(new ModsLocation(ModsSamples.ModsRaw), rig.Saved("AddAsync").Mods);
    }

    [Fact]
    public async Task Adding_a_mods_folder_to_a_game_that_had_none_confirms_that_folder_only()
    {
        var rig = RigWith(ModsSamples.SevenDays with { Mods = null });
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.ModsPath = ModsSamples.ModsRaw;

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Mods folder", ModsSamples.ModsResolved)], dialog.Folders);
    }

    [Fact]
    public async Task A_mods_folder_the_Api_reports_as_not_confirmed_is_asked_about_even_when_unchanged()
    {
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty()
            .WithGame(ModsSamples.SevenDays)
            .WithFolderStatus(
                ModsSamples.Id,
                Status(RepoRole.Install, ModsSamples.InstallPath, ModsSamples.InstallPath),
                Status(RepoRole.Mods, ModsSamples.ModsRaw, ModsSamples.ModsResolved, confirmed: false),
                Status(RepoRole.External, ModsSamples.SavesRaw, ModsSamples.SavesResolved))));
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.Equal([new ConsentFolder("Mods folder", ModsSamples.ModsResolved)], dialog.Folders);
    }

    [Fact]
    public async Task Editing_something_else_asks_nothing_about_a_mods_folder_that_is_confirmed_and_unchanged()
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.Name = "7 Days";

        var saving = editor.SaveCommand.ExecuteAsync(null);

        Assert.True(saving.IsCompleted);
        Assert.Empty(rig.Dialogs.Shown);
        await saving;
    }

    [Fact]
    public void A_template_draft_with_a_mods_folder_fills_the_mods_field()
    {
        var editor = EditorRig.Create().New(ModsSamples.SevenDays);

        Assert.Equal(ModsSamples.ModsRaw, editor.ModsPath);
    }

    [Fact]
    public async Task Choosing_a_template_fills_the_mods_field_and_choosing_another_without_one_clears_what_the_first_put_there()
    {
        var withMods = new GameTemplate("seven", "Seven Days", ModsSamples.SevenDays with { Id = "seven" });
        var without = new GameTemplate("plain", "Plain Game", new GameDefinition(Id: "plain", Name: "Plain Game", Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "plain.exe")));
        var rig = EditorRig.Create();
        ((FakeGamesApi)rig.Backend.Games).TemplatesOverride = [withMods, without];
        var editor = rig.New();

        await PickAsync(editor, "seven", "Seven Days");
        Assert.Equal(ModsSamples.ModsRaw, editor.ModsPath);

        await PickAsync(editor, "plain", "Plain Game");
        Assert.Equal(string.Empty, editor.ModsPath);
    }

    private static async Task PickAsync(GameEditorViewModel editor, string typed, string name)
    {
        editor.Name = typed;
        var match = Assert.Single(editor.NameMatches, item => item.Name == name);
        await ((IAsyncRelayCommand)match.Choose).ExecuteAsync(null);
    }

    [Fact]
    public async Task A_mods_folder_that_is_gone_shows_the_moved_or_gone_card_under_the_mods_field()
    {
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty()
            .WithGame(ModsSamples.SevenDays)
            .WithFolderStatus(
                ModsSamples.Id,
                Status(RepoRole.Install, ModsSamples.InstallPath, ModsSamples.InstallPath),
                Status(RepoRole.Mods, ModsSamples.ModsRaw, ModsSamples.ModsResolved, exists: false),
                Status(RepoRole.External, ModsSamples.SavesRaw, ModsSamples.SavesResolved))));
        var editor = rig.Edit(ModsSamples.Id);

        await editor.LoadAsync();

        Assert.NotNull(editor.ModsCard);
        Assert.Contains("mods folder", editor.ModsCard.Body);
        Assert.Equal(ModsSamples.ModsResolved, editor.ModsCard.Path);
        Assert.Null(editor.InstallCard);
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.ModsConfirmation);
        Assert.NotNull(editor.SavesConfirmation);
    }

    [Fact]
    public async Task Choosing_the_mods_folder_from_the_card_sets_the_mods_path_and_clears_the_card()
    {
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty()
            .WithGame(ModsSamples.SevenDays)
            .WithFolderStatus(
                ModsSamples.Id,
                Status(RepoRole.Install, ModsSamples.InstallPath, ModsSamples.InstallPath),
                Status(RepoRole.Mods, ModsSamples.ModsRaw, ModsSamples.ModsResolved, exists: false),
                Status(RepoRole.External, ModsSamples.SavesRaw, ModsSamples.SavesResolved))));
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        rig.Dialogs.AnswerFolder(@"D:\Mods");

        editor.ModsCard!.Actions[0].Command.Execute(null);

        Assert.Equal(@"D:\Mods", editor.ModsPath);
        Assert.Null(editor.ModsCard);
    }

    [Fact]
    public async Task A_confirmed_mods_folder_shows_when_it_was_confirmed_and_pointing_elsewhere_hides_the_line()
    {
        var editor = RigWith(ModsSamples.SevenDays).Edit(ModsSamples.Id);

        await editor.LoadAsync();
        Assert.Equal("Confirmed for SavePoint", editor.ModsConfirmation);

        editor.ModsPath = @"D:\Mods";
        Assert.Null(editor.ModsConfirmation);
    }

    [Fact]
    public async Task A_mods_folder_with_a_shortcut_shows_where_it_resolves_to()
    {
        var editor = RigWith(ModsSamples.SevenDays).Edit(ModsSamples.Id);

        await editor.LoadAsync();

        Assert.Equal($"Full location: {ModsSamples.ModsResolved}", editor.ModsResolves);

        editor.ModsPath = @"D:\Mods";
        Assert.Null(editor.ModsResolves);
    }

    [Fact]
    public async Task Browsing_for_the_mods_folder_opens_at_the_resolved_folder_and_takes_the_pick()
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        rig.Dialogs.AnswerFolder(@"D:\Mods");

        await editor.BrowseModsCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.FolderPicks);
        Assert.Equal(ModsSamples.ModsResolved, request.StartPath);
        Assert.Equal("Choose the mods folder", request.Title);
        Assert.Equal(@"D:\Mods", editor.ModsPath);
    }

    [Fact]
    public void Choosing_a_common_location_for_the_mods_changes_the_mods_field_and_not_the_saves_field()
    {
        var editor = EditorRig.Create().New();
        editor.SavesPath = @"{appData}\Saves";
        editor.ModsPath = @"{appData}\Mods";

        editor.ModsLocationChoices[0].Use.Execute(null);

        Assert.Equal(@"{documents}\Mods", editor.ModsPath);
        Assert.Equal(@"{appData}\Saves", editor.SavesPath);
        Assert.Equal(editor.LocationChoices.Select(choice => choice.Text), editor.ModsLocationChoices.Select(choice => choice.Text));
    }

    [Theory]
    [InlineData("validation.game.pathsNested")]
    [InlineData("validation.game.modsPathRequired")]
    public async Task A_problem_with_the_mods_path_is_shown_under_the_mods_field(string messageId)
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.Name = "7 Days";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new ValidationException([new ValidationIssue("mods.path", messageId)])));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(editor.ModsError);
        Assert.Null(editor.SavesError);
        Assert.Null(editor.InstallError);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task A_mods_folder_inside_the_saves_folder_is_refused_under_the_mods_field_before_anything_is_saved()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "7 Days to Die";
        editor.InstallPath = ModsSamples.InstallPath;
        editor.SavesPath = @"{appData}\7DaysToDie";
        editor.ModsPath = ModsSamples.ModsRaw;
        editor.ExePath = "7DaysToDie.exe";

        // Refused before any question is asked, so the save has finished without a dialog.
        var saving = editor.SaveCommand.ExecuteAsync(null);

        Assert.True(saving.IsCompleted, "The save should be refused without asking anything.");
        Assert.Equal("A game's folders can't be the same folder or inside one another.", editor.ModsError);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Theory]
    [InlineData("error.gameService.pathOverlap")]
    [InlineData("error.consentMarker.conflict")]
    [InlineData("error.pathSafety.tier1Blocked")]
    public async Task An_error_the_Api_reports_for_the_mods_folder_lands_under_the_mods_field(string messageId)
    {
        var rig = RigWith(ModsSamples.SevenDays with { Mods = null });
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.ModsPath = ModsSamples.ModsRaw;
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", new ErrorInfo(messageId, new Dictionary<string, object?> { ["role"] = "mods", ["path"] = ModsSamples.ModsResolved, ["conflictingGameId"] = "other-game", ["conflictingGameName"] = "Other Game" }, string.Empty));

        var (saving, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await saving;

        Assert.NotNull(editor.ModsCard);
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.InstallCard);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task A_mods_folder_with_no_confirmation_marker_is_asked_about_again_on_the_next_save()
    {
        var rig = RigWith(ModsSamples.SevenDays);
        var editor = rig.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        editor.Name = "7 Days";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Missing, ModsSamples.ModsResolved, RepoRole.Mods, null, null)));
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(editor.ModsCard);

        var (second, dialog) = SaveUntilAsked(rig, editor);
        dialog.ConfirmCommand.Execute(null);
        await second;

        Assert.Equal([new ConsentFolder("Mods folder", ModsSamples.ModsResolved)], dialog.Folders);
    }

    [Fact]
    public async Task A_path_that_cannot_be_resolved_in_the_mods_field_is_reported_under_that_field()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "7 Days to Die";
        editor.InstallPath = ModsSamples.InstallPath;
        editor.ModsPath = @"D:\Mods";
        editor.ExePath = "7DaysToDie.exe";
        var unknownToken = ErrorInfo.From(new ValidationException([new ValidationIssue("path", "validation.path.unknownToken")]));
        // The folders are resolved in the order the fields are shown: the install folder first, then the mods folder.
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", unknownToken);
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", unknownToken);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(editor.InstallError);
        Assert.NotNull(editor.ModsError);
        Assert.Null(editor.SavesError);
    }
}
