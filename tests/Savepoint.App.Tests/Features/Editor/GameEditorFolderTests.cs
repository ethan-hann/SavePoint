using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorFolderTests
{
    private static FolderStatus Status(RepoRole role, string raw, bool exists = true, bool confirmed = true, DateTimeOffset? at = null, string? resolved = null) =>
        new(role, raw, resolved ?? raw, exists, new ConsentStatus(confirmed, at));

    private static EditorRig RigFor(GameDefinition game, params FolderStatus[] statuses) =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game).WithFolderStatus(game.Id, statuses)));

    [Fact]
    public async Task Each_confirmed_folder_shows_when_it_was_confirmed()
    {
        var editor = EditorRig.Create().Edit();

        await editor.LoadAsync();

        Assert.Equal("Confirmed for SavePoint on Jul 2, 2026", editor.InstallConfirmation);
        Assert.Equal("Confirmed for SavePoint on Jul 2, 2026", editor.SavesConfirmation);
    }

    [Fact]
    public async Task A_confirmation_with_no_date_says_only_that_it_was_confirmed()
    {
        var game = SampleScenario.Skyrim;
        var editor = RigFor(game, Status(RepoRole.Install, game.Install.Path), Status(RepoRole.External, game.External!.Path)).Edit(game.Id);

        await editor.LoadAsync();

        Assert.Equal("Confirmed for SavePoint", editor.InstallConfirmation);
    }

    [Fact]
    public async Task A_folder_that_is_not_confirmed_shows_no_line()
    {
        var game = SampleScenario.Skyrim;
        var editor = RigFor(game, Status(RepoRole.Install, game.Install.Path, confirmed: false), Status(RepoRole.External, game.External!.Path)).Edit(game.Id);

        await editor.LoadAsync();

        Assert.Null(editor.InstallConfirmation);
        Assert.NotNull(editor.SavesConfirmation);
    }

    [Fact]
    public async Task A_new_game_reads_no_status_and_shows_no_line()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await editor.LoadAsync();

        Assert.Null(editor.InstallConfirmation);
        Assert.Empty(rig.Calls("GetFolderStatusAsync"));
    }

    [Fact]
    public async Task A_status_that_cannot_be_read_is_reported_in_a_toast_and_the_editor_still_works()
    {
        var rig = EditorRig.Create();
        rig.Backend.FailNext<IGamesApi>("GetFolderStatusAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var editor = rig.Edit();

        await editor.LoadAsync();

        Assert.Single(rig.Toasts.Toasts);
        Assert.Null(editor.InstallConfirmation);
        Assert.True(editor.CanSave);
    }

    [Fact]
    public async Task Pointing_a_folder_somewhere_else_hides_its_confirmation_and_pointing_back_shows_it_again()
    {
        var editor = EditorRig.Create().Edit();
        await editor.LoadAsync();

        editor.InstallPath = @"D:\Games\SkyrimSE";
        Assert.Null(editor.InstallConfirmation);
        Assert.NotNull(editor.SavesConfirmation);

        editor.InstallPath = SampleScenario.SkyrimInstall;
        Assert.NotNull(editor.InstallConfirmation);

        editor.SavesPath = @"{documents}\Elsewhere";
        Assert.Null(editor.SavesConfirmation);
    }

    [Fact]
    public async Task A_folder_that_is_gone_shows_the_moved_or_gone_card_with_its_path_and_a_way_to_choose_another()
    {
        var game = SampleScenario.Caretaker;
        var rig = RigFor(game, Status(RepoRole.Install, game.Install.Path, exists: false, at: ScenarioBuilder.DefaultNow), Status(RepoRole.External, game.External!.Path, resolved: @"C:\Users\you\AppData\Local\TheLastCaretaker\Saved\SaveGames"));
        var editor = rig.Edit(game.Id);

        await editor.LoadAsync();

        Assert.NotNull(editor.InstallCard);
        Assert.Equal("This game's folder has moved or is gone", editor.InstallCard.Title);
        Assert.Contains("install folder", editor.InstallCard.Body);
        Assert.Equal(@"C:\Games\The Last Caretaker", editor.InstallCard.Path);
        var choose = Assert.Single(editor.InstallCard.Actions);
        Assert.Equal("Choose the folder", choose.Label);
        Assert.True(choose.IsPrimary);
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.InstallConfirmation);
        Assert.NotNull(editor.SavesConfirmation);
    }

    [Fact]
    public async Task Choosing_the_folder_from_the_card_sets_the_path_and_clears_the_card()
    {
        var game = SampleScenario.Caretaker;
        var rig = RigFor(game, Status(RepoRole.Install, game.Install.Path, exists: false), Status(RepoRole.External, game.External!.Path));
        var editor = rig.Edit(game.Id);
        await editor.LoadAsync();
        rig.Dialogs.AnswerFolder(@"D:\Games\The Last Caretaker");

        editor.InstallCard!.Actions[0].Command.Execute(null);

        Assert.Equal(@"D:\Games\The Last Caretaker", editor.InstallPath);
        Assert.Null(editor.InstallCard);
        Assert.Equal(@"C:\Games\The Last Caretaker", Assert.Single(rig.Dialogs.FolderPicks).StartPath);
    }

    [Fact]
    public async Task A_saves_folder_that_is_gone_shows_the_card_under_the_saves_field()
    {
        var game = SampleScenario.Caretaker;
        var rig = RigFor(game, Status(RepoRole.Install, game.Install.Path), Status(RepoRole.External, game.External!.Path, exists: false));
        var editor = rig.Edit(game.Id);

        await editor.LoadAsync();

        Assert.Null(editor.InstallCard);
        Assert.NotNull(editor.SavesCard);
        Assert.Contains("saves folder", editor.SavesCard.Body);
    }

    [Fact]
    public async Task A_saves_folder_with_a_shortcut_shows_where_it_resolves_to()
    {
        var editor = EditorRig.Create().Edit();

        await editor.LoadAsync();

        Assert.Equal(@"Full location: C:\Users\you\Documents\My Games\Skyrim Special Edition\Saves", editor.SavesResolves);
    }

    [Fact]
    public void The_resolved_line_follows_the_field_and_disappears_for_a_plain_path()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        editor.SavesPath = @"{appData}\Hollow Knight";
        Assert.Equal(@"Full location: C:\Users\you\AppData\Roaming\Hollow Knight", editor.SavesResolves);

        editor.SavesPath = @"{localAppData}\Hollow Knight";
        Assert.Equal(@"Full location: C:\Users\you\AppData\Local\Hollow Knight", editor.SavesResolves);

        editor.SavesPath = @"D:\Saves";
        Assert.Null(editor.SavesResolves);
        Assert.Equal(2, rig.Calls("ResolvePathAsync").Count);
    }

    [Fact]
    public void A_shortcut_that_cannot_be_resolved_shows_no_resolved_line_and_no_error_while_typing()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", ErrorInfo.From(new ValidationException([new ValidationIssue("path", "validation.path.unknownToken")])));

        editor.SavesPath = "{docu";

        Assert.Null(editor.SavesResolves);
        Assert.Null(editor.SavesError);
    }

    [Theory]
    [InlineData("windows", new[] { "{documents}", "{savedGames}", "{appData}", "{localAppData}" })]
    [InlineData("linux", new[] { "{documents}", "{userHome}", "{appData}", "{localAppData}" })]
    [InlineData("macos", new[] { "{documents}", "{userHome}", "{appData}", "{localAppData}" })]
    public void The_common_locations_offered_are_the_ones_the_operating_system_has(string os, string[] expected)
    {
        var editor = EditorRig.Create(os: os).New();

        Assert.Equal(expected, editor.LocationChoices.Select(choice => choice.Text));
    }

    [Theory]
    [InlineData("windows", new[] { "Documents", "Saved Games", "AppData", "Local AppData" })]
    [InlineData("linux", new[] { "Documents", "Home folder", "AppData", "Local AppData" })]
    [InlineData("macos", new[] { "Documents", "Home folder", "AppData", "Local AppData" })]
    public void The_common_locations_are_offered_under_friendly_names_that_still_insert_the_shortcut(string os, string[] expected)
    {
        var editor = EditorRig.Create(os: os).New();

        Assert.Equal(expected, editor.LocationChoices.Select(choice => choice.Display));
        Assert.All(editor.LocationChoices, choice => Assert.StartsWith("{", choice.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "{documents}")]
    [InlineData(@"{appData}\Hollow Knight", @"{documents}\Hollow Knight")]
    [InlineData("{appData}", "{documents}")]
    [InlineData(@"My Games\Skyrim", @"{documents}\My Games\Skyrim")]
    [InlineData("  {localAppData}/Saves ", "{documents}/Saves")]
    public void Choosing_a_common_location_puts_its_shortcut_at_the_start_in_place_of_any_other(string current, string expected)
    {
        var editor = EditorRig.Create().New();
        editor.SavesPath = current;

        editor.LocationChoices[0].Use.Execute(null);

        Assert.Equal(expected, editor.SavesPath);
    }

    [Fact]
    public async Task Browsing_for_the_install_folder_opens_at_the_current_one_and_takes_the_pick()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        rig.Dialogs.AnswerFolder(@"D:\Games\SkyrimSE");

        await editor.BrowseInstallCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.FolderPicks);
        Assert.Equal(SampleScenario.SkyrimInstall, request.StartPath);
        Assert.Equal("Choose the game's install folder", request.Title);
        Assert.Equal(@"D:\Games\SkyrimSE", editor.InstallPath);
    }

    [Fact]
    public async Task Cancelling_the_folder_picker_changes_nothing()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();

        await editor.BrowseInstallCommand.ExecuteAsync(null);
        await editor.BrowseSavesCommand.ExecuteAsync(null);

        Assert.Equal(SampleScenario.SkyrimInstall, editor.InstallPath);
        Assert.Equal(SampleScenario.SkyrimSavesRaw, editor.SavesPath);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Browsing_for_the_saves_opens_at_the_resolved_shortcut_and_takes_the_pick()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        rig.Dialogs.AnswerFolder(@"C:\Users\you\Documents\My Games\Other");

        await editor.BrowseSavesCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.FolderPicks);
        Assert.Equal(SampleScenario.SkyrimSaves, request.StartPath);
        Assert.Equal("Choose the saves folder", request.Title);
        Assert.Equal(@"C:\Users\you\Documents\My Games\Other", editor.SavesPath);
    }

    [Fact]
    public async Task A_picker_for_an_empty_field_opens_with_no_start_folder()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await editor.BrowseInstallCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(rig.Dialogs.FolderPicks).StartPath);
    }
}
