using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorErrorTests
{
    private static async Task Save(EditorRig rig, GameEditorViewModel editor)
    {
        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;
    }

    private static GameEditorViewModel Filled(EditorRig rig)
    {
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.SavesPath = @"{appData}\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        return editor;
    }

    // The Api names the folder at fault the way it does for the real backend: a role in the error's arguments.
    private static ErrorInfo OnFolder(ErrorInfo error, RepoRole role) =>
        error with { Args = new Dictionary<string, object?>(error.Args) { ["role"] = role } };

    private static ErrorInfo Invalid(params (string Path, string MessageId)[] issues) =>
        ErrorInfo.From(new ValidationException([.. issues.Select(issue => new ValidationIssue(issue.Path, issue.MessageId))]));

    [Fact]
    public async Task A_missing_start_file_is_flagged_on_its_field_and_nothing_is_saved()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.ExePath = "  ";

        await Save(rig, editor);

        Assert.Equal("Choose the file that starts the game.", editor.ExePathError);
        Assert.Empty(rig.Calls("AddAsync"));
        Assert.Empty(rig.Shell.Calls);
        Assert.Empty(rig.Dialogs.Shown);
    }

    [Theory]
    [InlineData(@"C:\Games\Hollow Knight\hk.exe", "Enter the start file's location relative to the game folder, not a full path.")]
    [InlineData(@"..\hk.exe", "The start file has to be inside the game folder.")]
    public async Task A_start_file_that_is_absolute_or_climbs_out_is_refused_on_its_field(string exePath, string message)
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.ExePath = exePath;

        await Save(rig, editor);

        Assert.Equal(message, editor.ExePathError);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Theory]
    [InlineData("", "Enter the launch link, such as steam://run/123456.")]
    [InlineData("not a link", "That isn't a launch link. It should start with a name and a colon, such as steam://.")]
    public async Task The_Steam_choice_needs_a_launch_link_that_looks_like_one(string uri, string message)
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.LaunchChoice = 1;
        editor.Uri = uri;

        await Save(rig, editor);

        Assert.Equal(message, editor.UriError);
        Assert.Null(editor.ExePathError);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task A_saves_folder_inside_the_install_folder_is_flagged_on_the_saves_field()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.SavesPath = @"C:\Games\Hollow Knight\Saves";

        await Save(rig, editor);

        Assert.Equal("The game folder and the saves folder can't be inside each other.", editor.SavesError);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task Every_problem_is_shown_at_once_each_on_its_own_field()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.ExePath = "";
        editor.SavesPath = @"C:\Games\Hollow Knight\Saves";

        await Save(rig, editor);

        Assert.NotNull(editor.ExePathError);
        Assert.NotNull(editor.SavesError);
        Assert.Null(editor.InstallError);
        Assert.Null(editor.NameError);
    }

    [Fact]
    public async Task Editing_a_field_clears_its_own_error_only()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.ExePath = "";
        editor.SavesPath = @"C:\Games\Hollow Knight\Saves";
        await Save(rig, editor);

        editor.ExePath = "hollow_knight.exe";

        Assert.Null(editor.ExePathError);
        Assert.NotNull(editor.SavesError);

        editor.SavesPath = @"D:\Saves";

        Assert.Null(editor.SavesError);
    }

    [Fact]
    public async Task Field_issues_from_the_Api_land_on_the_matching_fields()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", Invalid(
            ("name", ValidationMessageIds.GameNameRequired),
            ("install.path", ValidationMessageIds.GameInstallPathRequired),
            ("external.path", ValidationMessageIds.GameExternalPathRequired),
            ("launch.method", ValidationMessageIds.LaunchMethodInvalid),
            ("launch.exePath", ValidationMessageIds.LaunchExePathRequired),
            ("launch.uri", ValidationMessageIds.LaunchUriRequired)));

        await Save(rig, editor);

        Assert.Equal("Give the game a name.", editor.NameError);
        Assert.Equal("Choose the folder where the game is installed.", editor.InstallError);
        Assert.Equal("Choose the saves folder, or remove the saves location.", editor.SavesError);
        Assert.Equal("Choose how SavePoint should start the game.", editor.LaunchError);
        Assert.Equal("Choose the file that starts the game.", editor.ExePathError);
        Assert.Equal("Enter the launch link, such as steam://run/123456.", editor.UriError);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task An_issue_on_no_field_becomes_a_card_above_the_form()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", Invalid(("schemaVersion", ValidationMessageIds.GameSchemaVersionUnsupported)));

        await Save(rig, editor);

        Assert.NotNull(editor.FormError);
        Assert.Equal("Some fields need another look", editor.FormError.Title);
        Assert.Contains("different version of SavePoint", editor.FormError.Body);
    }

    [Fact]
    public async Task A_folder_too_broad_to_track_is_explained_under_the_folder_it_names()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.InstallPath = @"C:\Program Files (x86)";
        rig.Backend.FailNext<IGamesApi>("AddAsync", SampleScenario.FolderTooBroadError);

        await Save(rig, editor);

        Assert.NotNull(editor.InstallCard);
        Assert.Equal("That folder is too broad to use", editor.InstallCard.Title);
        Assert.Equal(@"C:\Program Files (x86)", editor.InstallCard.Path);
        Assert.Null(editor.SavesCard);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task A_refused_path_that_matches_the_saves_folder_lands_under_the_saves_field()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.SavesPath = @"{documents}\My Games";
        rig.Backend.FailNext<IGamesApi>("AddAsync", ErrorInfo.From(new PathSafetyException(@"C:\Users\you\Documents\My Games", 2)));

        await Save(rig, editor);

        Assert.NotNull(editor.SavesCard);
        Assert.Null(editor.InstallCard);
    }

    [Fact]
    public async Task A_folder_claimed_by_another_game_lands_under_the_role_it_names()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", ErrorInfo.From(new ConsentMarkerException(ConsentFailure.Conflict, @"C:\Users\you\AppData\Roaming\Hollow Knight", RepoRole.External, "other-game", RepoRole.Install)));

        await Save(rig, editor);

        Assert.Null(editor.InstallCard);
        Assert.NotNull(editor.SavesCard);
        Assert.Equal("That folder belongs to another game", editor.SavesCard.Title);
    }

    [Fact]
    public async Task A_save_that_fails_because_a_folder_is_gone_offers_to_choose_another()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.Name = "Skyrim SE";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", ErrorInfo.From(new PathMissingException(SampleScenario.SkyrimInstall, RepoRole.Install)));
        await Save(rig, editor);

        Assert.NotNull(editor.InstallCard);
        Assert.Equal("This game's folder has moved or is gone", editor.InstallCard.Title);
        var choose = Assert.Single(editor.InstallCard.Actions);
        Assert.Equal("Choose the folder", choose.Label);

        rig.Dialogs.AnswerFolder(@"D:\Games\SkyrimSE");
        choose.Command.Execute(null);

        Assert.Equal(@"D:\Games\SkyrimSE", editor.InstallPath);
        Assert.Null(editor.InstallCard);
    }

    [Fact]
    public async Task An_overlap_is_shown_under_the_install_folder_when_the_error_names_it()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", OnFolder(ErrorInfo.From(new GameOverlapException(SampleScenario.SatisfactoryId, "Satisfactory")), RepoRole.Install));

        await Save(rig, editor);

        Assert.NotNull(editor.InstallCard);
        Assert.Equal("That folder overlaps another game", editor.InstallCard.Title);
        Assert.Contains("Satisfactory", editor.InstallCard.Body);
        Assert.Null(editor.SavesCard);
    }

    [Fact]
    public async Task An_overlap_on_the_saves_folder_of_a_new_game_is_shown_under_the_saves_field()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", OnFolder(ErrorInfo.From(new GameOverlapException(SampleScenario.SatisfactoryId, "Satisfactory")), RepoRole.External));

        await Save(rig, editor);

        Assert.NotNull(editor.SavesCard);
        Assert.Equal("That folder overlaps another game", editor.SavesCard.Title);
        Assert.Null(editor.InstallCard);
    }

    [Fact]
    public async Task An_overlap_is_shown_under_the_folder_the_error_names_when_editing()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        editor.SavesPath = @"{localAppData}\FactoryGame";
        rig.Backend.FailNext<IGamesApi>("UpdateAsync", OnFolder(ErrorInfo.From(new GameOverlapException(SampleScenario.SatisfactoryId, "Satisfactory")), RepoRole.External));

        await Save(rig, editor);

        Assert.NotNull(editor.SavesCard);
        Assert.Null(editor.InstallCard);
    }

    [Fact]
    public async Task An_error_that_names_no_folder_is_shown_above_the_form_and_not_under_a_guessed_field()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", ErrorInfo.From(new GameOverlapException(SampleScenario.SatisfactoryId, "Satisfactory")));

        await Save(rig, editor);

        Assert.NotNull(editor.FormError);
        Assert.Equal("That folder overlaps another game", editor.FormError.Title);
        Assert.Null(editor.InstallCard);
        Assert.Null(editor.SavesCard);
    }

    [Fact]
    public async Task Any_other_failure_shows_one_card_above_the_form_with_its_detail_behind_the_toggle()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "System.IO.IOException: disk"));

        await Save(rig, editor);

        Assert.NotNull(editor.FormError);
        Assert.Equal("Something went wrong", editor.FormError.Title);
        Assert.Equal("System.IO.IOException: disk", editor.FormError.SupportText);
        Assert.Null(editor.InstallCard);
    }

    [Fact]
    public async Task The_form_card_can_be_dismissed()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        await Save(rig, editor);

        var dismiss = Assert.Single(editor.FormError!.Actions);
        dismiss.Command.Execute(null);

        Assert.Equal("Dismiss", dismiss.Label);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task Saving_again_clears_what_the_last_attempt_showed()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", Invalid(("name", ValidationMessageIds.GameNameRequired)));
        await Save(rig, editor);
        Assert.NotNull(editor.NameError);

        await Save(rig, editor);

        Assert.Null(editor.NameError);
        Assert.Null(editor.FormError);
        Assert.Equal(["refresh", $"select:{rig.Saved("AddAsync", 1).Id}", "close"], rig.Shell.Calls);
    }

    [Fact]
    public async Task A_path_that_cannot_be_resolved_is_flagged_on_its_field_before_any_confirmation()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.InstallPath = @"{bogus}\Games";
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", Invalid(("path", "validation.path.unknownToken")));

        await Save(rig, editor);

        Assert.Equal("SavePoint doesn't recognize a name in curly brackets in that location, or it isn't on this computer.", editor.InstallError);
        Assert.Null(editor.SavesError);
        Assert.Empty(rig.Dialogs.Shown);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task Both_folders_that_cannot_be_resolved_are_flagged_together()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", Invalid(("path", "validation.path.notAbsolute")));
        rig.Backend.FailNext<IGamesApi>("ResolvePathAsync", Invalid(("path", "validation.path.unknownToken")));

        await Save(rig, editor);

        Assert.Equal("Enter the folder's full location, starting from the top of the drive.", editor.InstallError);
        Assert.NotNull(editor.SavesError);
        Assert.Empty(rig.Dialogs.Shown);
    }
}
