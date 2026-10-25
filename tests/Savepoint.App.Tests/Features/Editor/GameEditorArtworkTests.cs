using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

// The editor's Artwork section: a preview, where the art came from, the user's own image, look again and the Steam app id.
// Everything is applied on Save like every other field.
public class GameEditorArtworkTests
{
    private const string _skyrim = SampleScenario.SkyrimId;

    private static EditorRig Rig(FakeBackend? backend = null) => EditorRig.Create(backend);

    private static IReadOnlyList<ApiCall> Calls(EditorRig rig, string member) => rig.Backend.Calls.To<IArtworkApi>(member);

    private static int IndexOf(EditorRig rig, string api, string member) =>
        rig.Backend.Calls.All.ToList().FindIndex(call => call.Api == api && call.Member == member);

    private static async Task SaveAsync(EditorRig rig, GameEditorViewModel editor)
    {
        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;
    }

    private static GameEditorViewModel FilledNew(EditorRig rig)
    {
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        return editor;
    }

    private static async Task<GameEditorViewModel> ChooseAsync(EditorRig rig, GameEditorViewModel editor, string path)
    {
        rig.Dialogs.AnswerOpenFile(path);
        await editor.ChooseImageCommand.ExecuteAsync(null);
        return editor;
    }

    // ---- What the section shows

    [Fact]
    public void A_game_with_no_artwork_says_so_and_offers_nothing_to_remove()
    {
        var rig = Rig();

        var editor = rig.Edit();

        Assert.Equal("No artwork found", editor.ArtworkSourceText);
        Assert.False(editor.ArtworkPreview.HasArtwork);
        Assert.False(editor.CanRemoveImage);
        Assert.Equal(string.Empty, editor.SteamAppIdText);
        Assert.Single(Calls(rig, nameof(IArtworkApi.GetAsync)));
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Steam_artwork_shows_in_the_preview_as_from_steam()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptSteamArrives(_skyrim, "/art/wide.jpg", "/art/square.jpg");

        var editor = Rig(backend).Edit();

        Assert.Equal("From Steam", editor.ArtworkSourceText);
        Assert.Equal("/art/wide.jpg", editor.ArtworkPreview.WidePath);
        Assert.Equal("/art/square.jpg", editor.ArtworkPreview.SquarePath);
        Assert.False(editor.CanRemoveImage);
    }

    [Fact]
    public void Gog_artwork_shows_in_the_preview_as_from_gog_galaxy_and_is_not_removable()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptGogArrives(_skyrim, "/art/wide.webp", "/art/square.webp");

        var editor = Rig(backend).Edit();

        Assert.Equal("From GOG Galaxy", editor.ArtworkSourceText);
        Assert.Equal("/art/wide.webp", editor.ArtworkPreview.WidePath);
        Assert.Equal("/art/square.webp", editor.ArtworkPreview.SquarePath);
        Assert.False(editor.CanRemoveImage);
    }

    [Fact]
    public void The_users_own_image_shows_as_your_image_and_can_be_removed()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptCustom(_skyrim, "/mine.png");

        var editor = Rig(backend).Edit();

        Assert.Equal("Your image", editor.ArtworkSourceText);
        Assert.Equal("/mine.png", editor.ArtworkPreview.WidePath);
        Assert.True(editor.CanRemoveImage);
        Assert.True(editor.RemoveImageCommand.CanExecute(null));
    }

    [Fact]
    public void A_new_game_starts_with_no_artwork_reads_nothing_and_cannot_look_again()
    {
        var rig = Rig();

        var editor = rig.New();

        Assert.Equal("No artwork found", editor.ArtworkSourceText);
        Assert.False(editor.CanLookAgain);
        Assert.False(editor.LookAgainCommand.CanExecute(null));
        Assert.Empty(Calls(rig, nameof(IArtworkApi.GetAsync)));
    }

    [Fact]
    public void Steam_artwork_that_arrives_while_the_editor_is_open_shows_in_its_preview()
    {
        var rig = Rig();

        // The game's tile on the table is what has the shell's artwork read for it.
        rig.Shell.Artwork.For(_skyrim).NotifyOnScreen();
        var editor = rig.Edit();
        Assert.False(editor.ArtworkPreview.HasArtwork);

        rig.Backend.Artwork.ScriptSteamArrives(_skyrim, "/art/wide.jpg", "/art/square.jpg");

        Assert.Equal("From Steam", editor.ArtworkSourceText);
        Assert.Equal("/art/wide.jpg", editor.ArtworkPreview.WidePath);
        Assert.Equal("/art/square.jpg", editor.ArtworkPreview.SquarePath);
    }

    [Fact]
    public void A_disposed_editor_no_longer_follows_the_artwork()
    {
        var rig = Rig();
        rig.Shell.Artwork.For(_skyrim).NotifyOnScreen();
        var editor = rig.Edit();
        editor.Dispose();

        rig.Backend.Artwork.ScriptSteamArrives(_skyrim, "/art/wide.jpg", "/art/square.jpg");

        Assert.False(editor.ArtworkPreview.HasArtwork);
    }

    [Fact]
    public async Task An_image_chosen_in_the_editor_stays_in_the_preview_when_steam_artwork_arrives()
    {
        var rig = Rig();
        rig.Shell.Artwork.For(_skyrim).NotifyOnScreen();
        var editor = await ChooseAsync(rig, rig.Edit(), "/mine.png");

        rig.Backend.Artwork.ScriptSteamArrives(_skyrim, "/art/wide.jpg", "/art/square.jpg");

        Assert.Equal("/mine.png", editor.ArtworkPreview.WidePath);
        Assert.Equal("Your image", editor.ArtworkSourceText);
    }

    // ---- The Steam app id

    [Fact]
    public void An_app_id_found_by_the_lookup_is_the_hint_when_the_field_is_empty()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptDetectedAppId(_skyrim, 489830);

        var editor = Rig(backend).Edit();

        Assert.Equal(string.Empty, editor.SteamAppIdText);
        Assert.Equal("489830 (found automatically)", editor.SteamAppIdHint);
    }

    [Fact]
    public void With_no_id_found_the_hint_is_an_example()
    {
        var editor = Rig().Edit();

        Assert.Equal("For example 489830", editor.SteamAppIdHint);
    }

    [Fact]
    public async Task A_stored_app_id_fills_the_field_and_is_not_called_detected()
    {
        var backend = FakeBackend.Sample();
        var game = FakeAssert.Ok(await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Single(g => g.Id == _skyrim);
        await backend.Games.UpdateAsync(game with { SteamAppId = 72850 });

        var editor = Rig(backend).Edit();

        Assert.Equal("72850", editor.SteamAppIdText);
        Assert.Equal("For example 489830", editor.SteamAppIdHint);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Saving_sends_the_typed_app_id_with_its_spaces_trimmed()
    {
        var rig = Rig();
        var editor = rig.Edit();

        editor.SteamAppIdText = "  489830  ";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(489830, rig.Saved("UpdateAsync").SteamAppId);
    }

    [Fact]
    public async Task Clearing_a_stored_app_id_saves_it_as_none()
    {
        var backend = FakeBackend.Sample();
        var game = FakeAssert.Ok(await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Single(g => g.Id == _skyrim);
        await backend.Games.UpdateAsync(game with { SteamAppId = 72850 });
        var rig = Rig(backend);
        var editor = rig.Edit();

        editor.SteamAppIdText = string.Empty;
        await editor.SaveCommand.ExecuteAsync(null);

        // The first update call is the test setting the stored id; the editor's save is the second.
        Assert.Null(rig.Saved("UpdateAsync", 1).SteamAppId);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("48 983")]
    [InlineData("99999999999")]
    [InlineData("4.5")]
    public async Task An_app_id_that_is_not_a_positive_number_is_refused_under_its_field_and_nothing_is_saved(string text)
    {
        var rig = Rig();
        var editor = rig.Edit();

        editor.SteamAppIdText = text;
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Enter the Steam app ID as a number above zero, such as 489830, or leave it empty.", editor.SteamAppIdError);
        Assert.Empty(rig.Calls("UpdateAsync"));
        Assert.Empty(rig.Shell.Calls);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("48 983")]
    [InlineData("0")]
    public void An_app_id_that_is_not_a_number_is_flagged_as_it_is_typed_and_cleared_when_it_is_fixed(string text)
    {
        var editor = Rig().Edit();

        editor.SteamAppIdText = text;
        Assert.Equal("Enter the Steam app ID as a number above zero, such as 489830, or leave it empty.", editor.SteamAppIdError);

        editor.SteamAppIdText = "489830";
        Assert.Null(editor.SteamAppIdError);

        editor.SteamAppIdText = text;
        editor.SteamAppIdText = string.Empty;
        Assert.Null(editor.SteamAppIdError);
    }

    [Fact]
    public async Task Typing_again_clears_the_app_id_problem()
    {
        var rig = Rig();
        var editor = rig.Edit();
        editor.SteamAppIdText = "abc";
        await editor.SaveCommand.ExecuteAsync(null);

        editor.SteamAppIdText = "489";

        Assert.Null(editor.SteamAppIdError);
    }

    [Fact]
    public void Changing_only_the_app_id_makes_the_form_changed_and_turns_look_again_off_until_it_is_back()
    {
        var editor = Rig().Edit();
        Assert.True(editor.CanLookAgain);

        editor.SteamAppIdText = "489830";
        Assert.True(editor.IsDirty);
        Assert.False(editor.CanLookAgain);
        Assert.False(editor.LookAgainCommand.CanExecute(null));

        editor.SteamAppIdText = string.Empty;
        Assert.False(editor.IsDirty);
        Assert.True(editor.CanLookAgain);
    }

    // ---- Choosing and removing the user's own image

    [Fact]
    public async Task Choosing_an_image_previews_it_marks_the_form_changed_and_touches_nothing_yet()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("mine.png", 300, 200);
        var rig = Rig();
        var editor = rig.Edit();

        await ChooseAsync(rig, editor, file);

        Assert.Equal("Your image", editor.ArtworkSourceText);
        Assert.Equal(file, editor.ArtworkPreview.WidePath);
        Assert.Equal(file, editor.ArtworkPreview.SquarePath);
        Assert.True(editor.CanRemoveImage);
        Assert.True(editor.IsDirty);
        Assert.Empty(Calls(rig, nameof(IArtworkApi.SetCustomImageAsync)));
        var pick = Assert.Single(rig.Dialogs.OpenFilePicks);
        Assert.Equal("Choose a picture for this game", pick.Title);
        Assert.Equal(["*.png", "*.jpg", "*.jpeg", "*.webp"], Assert.Single(pick.Filters!).Patterns);
    }

    [Fact]
    public async Task Cancelling_the_picker_changes_nothing()
    {
        var rig = Rig();
        var editor = rig.Edit();

        await editor.ChooseImageCommand.ExecuteAsync(null);

        Assert.Equal("No artwork found", editor.ArtworkSourceText);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Saving_an_existing_game_saves_it_then_copies_the_image_in_then_closes()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("mine.png", 300, 200);
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), file);

        await editor.SaveCommand.ExecuteAsync(null);

        var set = Assert.Single(Calls(rig, nameof(IArtworkApi.SetCustomImageAsync)));
        Assert.Equal([_skyrim, file], set.Args.Cast<string>());
        Assert.True(IndexOf(rig, nameof(IGamesApi), nameof(IGamesApi.UpdateAsync)) < IndexOf(rig, nameof(IArtworkApi), nameof(IArtworkApi.SetCustomImageAsync)));
        Assert.Contains("close", rig.Shell.Calls);
        Assert.Equal(ArtworkSource.Custom, FakeAssert.Ok(await rig.Backend.Artwork.GetAsync(_skyrim, TestContext.Current.CancellationToken)).Source);
    }

    [Fact]
    public async Task Saving_a_new_game_copies_the_image_in_after_the_game_exists_under_its_new_id()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("mine.png", 300, 200);
        var rig = Rig();
        var editor = await ChooseAsync(rig, FilledNew(rig), file);

        await SaveAsync(rig, editor);

        var added = rig.Saved("AddAsync");
        var set = Assert.Single(Calls(rig, nameof(IArtworkApi.SetCustomImageAsync)));
        Assert.Equal([added.Id, file], set.Args.Cast<string>());
        Assert.True(IndexOf(rig, nameof(IGamesApi), nameof(IGamesApi.AddAsync)) < IndexOf(rig, nameof(IArtworkApi), nameof(IArtworkApi.SetCustomImageAsync)));
        Assert.Equal(["refresh", $"select:{added.Id}", "close"], rig.Shell.Calls);
    }

    [Fact]
    public async Task Removing_the_users_image_is_applied_on_save_not_before()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptCustom(_skyrim, "/mine.png");
        var rig = Rig(backend);
        var editor = rig.Edit();

        editor.RemoveImageCommand.Execute(null);

        Assert.Equal("No artwork found", editor.ArtworkSourceText);
        Assert.False(editor.ArtworkPreview.HasArtwork);
        Assert.False(editor.CanRemoveImage);
        Assert.True(editor.IsDirty);
        Assert.Empty(Calls(rig, nameof(IArtworkApi.ClearCustomImageAsync)));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(Calls(rig, nameof(IArtworkApi.ClearCustomImageAsync)));
        Assert.Equal(ArtworkSource.None, FakeAssert.Ok(await backend.Artwork.GetAsync(_skyrim, TestContext.Current.CancellationToken)).Source);
    }

    [Fact]
    public async Task Removing_an_image_that_was_only_chosen_just_forgets_it_and_the_form_is_unchanged_again()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), fixtures.Png("mine.png", 300, 200));

        editor.RemoveImageCommand.Execute(null);
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.False(editor.CanRemoveImage);
        Assert.Empty(Calls(rig, nameof(IArtworkApi.SetCustomImageAsync)));
        Assert.Empty(Calls(rig, nameof(IArtworkApi.ClearCustomImageAsync)));
    }

    [Fact]
    public async Task Choosing_after_removing_replaces_the_removal_with_the_new_image()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("new.png", 300, 200);
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptCustom(_skyrim, "/mine.png");
        var rig = Rig(backend);
        var editor = rig.Edit();
        editor.RemoveImageCommand.Execute(null);

        await ChooseAsync(rig, editor, file);
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Empty(Calls(rig, nameof(IArtworkApi.ClearCustomImageAsync)));
        Assert.Equal(file, Assert.Single(Calls(rig, nameof(IArtworkApi.SetCustomImageAsync))).Args[1]);
    }

    [Fact]
    public async Task Going_back_with_a_chosen_image_asks_before_throwing_it_away()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), fixtures.Png("mine.png", 300, 200));
        rig.Dialogs.AnswerConfirm(false);

        await editor.BackCommand.ExecuteAsync(null);

        Assert.Single(rig.Dialogs.Confirmations);
        Assert.Empty(rig.Shell.Calls);
    }

    // ---- When the image is refused

    [Fact]
    public async Task A_file_that_is_not_an_image_leaves_the_game_saved_the_pane_open_and_names_the_problem()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), fixtures.NotAnImage("page.png"));
        editor.Name = "Skyrim SE";

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Skyrim SE", rig.Saved("UpdateAsync").Name);
        Assert.NotNull(editor.ArtworkError);
        Assert.Equal("That file isn't a picture SavePoint can use", editor.ArtworkError.Title);
        Assert.Contains("PNG, JPEG, and WebP", editor.ArtworkError.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("close", rig.Shell.Calls);
        Assert.Contains("refresh", rig.Shell.Calls);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public async Task A_picture_that_is_too_big_is_named_with_its_limit()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), fixtures.Png("big.png", 300, 200));
        rig.Backend.FailNext<IArtworkApi>(nameof(IArtworkApi.SetCustomImageAsync), ErrorInfo.From(new ArtworkTooLargeException("big.png", 25)));

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("That picture is too big", editor.ArtworkError!.Title);
        Assert.Contains("25 MB", editor.ArtworkError.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_a_refusal_for_a_new_game_saving_again_updates_the_game_instead_of_adding_it_twice()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, FilledNew(rig), fixtures.NotAnImage("page.png"));
        await SaveAsync(rig, editor);
        Assert.NotNull(editor.ArtworkError);
        var good = fixtures.Png("good.png", 300, 200);
        await ChooseAsync(rig, editor, good);

        await SaveAsync(rig, editor);

        var added = rig.Saved("AddAsync");
        Assert.Equal(added.Id, rig.Saved("UpdateAsync").Id);
        Assert.Null(editor.ArtworkError);
        Assert.Equal(good, Calls(rig, nameof(IArtworkApi.SetCustomImageAsync))[^1].Args[1]);
        Assert.Contains("close", rig.Shell.Calls);
        Assert.True(editor.IsEditing);
    }

    [Fact]
    public async Task Dismissing_the_artwork_problem_clears_its_card()
    {
        using var fixtures = new ArtFixtures();
        var rig = Rig();
        var editor = await ChooseAsync(rig, rig.Edit(), fixtures.NotAnImage("page.png"));
        await editor.SaveCommand.ExecuteAsync(null);

        editor.DismissArtworkErrorCommand.Execute(null);

        Assert.Null(editor.ArtworkError);
    }

    // ---- Look again

    [Fact]
    public async Task Look_again_asks_the_api_and_shows_what_it_found()
    {
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptSteamFinds(_skyrim, "/art/wide.jpg", "/art/square.jpg");
        var rig = Rig(backend);
        var editor = rig.Edit();
        Assert.Equal("No artwork found", editor.ArtworkSourceText);

        await editor.LookAgainCommand.ExecuteAsync(null);

        Assert.Equal(_skyrim, Assert.Single(Calls(rig, nameof(IArtworkApi.LookAgainAsync))).Args[0]);
        Assert.Equal("From Steam", editor.ArtworkSourceText);
        Assert.Equal("/art/wide.jpg", editor.ArtworkPreview.WidePath);
        Assert.False(editor.IsLookingAgain);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task A_look_again_that_fails_says_why_and_changes_nothing()
    {
        var rig = Rig();
        var editor = rig.Edit();
        rig.Backend.FailNext<IArtworkApi>(nameof(IArtworkApi.LookAgainAsync), ErrorInfo.From(new GameNotFoundException(_skyrim)));

        await editor.LookAgainCommand.ExecuteAsync(null);

        Assert.NotNull(editor.ArtworkError);
        Assert.Equal("No artwork found", editor.ArtworkSourceText);
        Assert.False(editor.IsLookingAgain);
    }

    [Fact]
    public async Task Look_again_does_not_disturb_an_image_that_was_chosen_and_not_yet_saved()
    {
        using var fixtures = new ArtFixtures();
        var file = fixtures.Png("mine.png", 300, 200);
        var backend = FakeBackend.Sample();
        backend.Artwork.ScriptSteamFinds(_skyrim, "/art/wide.jpg", "/art/square.jpg");
        var rig = Rig(backend);
        var editor = await ChooseAsync(rig, rig.Edit(), file);

        await editor.LookAgainCommand.ExecuteAsync(null);

        Assert.Equal("Your image", editor.ArtworkSourceText);
        Assert.Equal(file, editor.ArtworkPreview.WidePath);
    }

    // ---- Import

    [Fact]
    public async Task An_imported_definition_brings_its_app_id_into_the_field()
    {
        const string file = @"C:\Downloads\hollow-knight.json";
        var imported = new GameDefinition(
            Id: "from-file-ab12",
            Name: "Hollow Knight",
            Install: new InstallLocation(@"C:\Games\Hollow Knight"),
            Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "hollow_knight.exe"),
            SteamAppId: 367520);
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(file, imported)));
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(file);

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Equal("367520", editor.SteamAppIdText);
    }
}
