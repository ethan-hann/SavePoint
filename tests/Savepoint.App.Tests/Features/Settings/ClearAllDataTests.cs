using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

public class ClearAllDataTests
{
    private static readonly ClearAllDataPreview _preview = new(
        SampleScenario.StorageRoot,
        79_300_000_000,
        3,
        6,
        @"C:\Users\you\AppData\Roaming\SavePoint",
        [@"C:\Games\SkyrimSE", @"C:\Users\you\Documents\My Games\Skyrim Special Edition\Saves"],
        BlockedBy: null);

    private static ClearAllDataSectionViewModel Section(SettingsRig rig) => rig.Screen.ClearAll;

    private static FakeBackend WithPreview(ClearAllDataPreview preview) => new(ScenarioBuilder.Sample().WithClearAllDataPreview(preview));

    private static ClearAllDataDialogViewModel Dialog(SettingsRig rig) => Assert.IsType<ClearAllDataDialogViewModel>(Assert.Single(rig.Dialogs.Shown));

    // Runs the section's command up to the point the dialog is waiting for an answer.
    private static Task StartAsync(SettingsRig rig) => Section(rig).RemoveCommand.ExecuteAsync(null);

    // Types the word and confirms, the way the user does.
    private static void Confirm(ClearAllDataDialogViewModel dialog)
    {
        dialog.TypedText = dialog.Word;
        dialog.ConfirmCommand.Execute(null);
    }

    // ---- The flow

    [Fact]
    public async Task The_button_reads_what_would_go_before_it_asks_anything()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));

        var run = StartAsync(rig);

        // The question is worded from what the preview found, so it could not have been asked before the read.
        Assert.Contains("79.3 GB", Dialog(rig).Freed, StringComparison.Ordinal);
        Dialog(rig).CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task A_preview_that_cannot_be_read_shows_why_and_asks_nothing()
    {
        var rig = await SettingsRig.ShownAsync();
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetClearAllDataPreviewAsync), ErrorInfo.From(new InvalidOperationException("No preview.")));

        await StartAsync(rig);

        Assert.NotNull(Section(rig).Problem);
        Assert.Empty(rig.Dialogs.Shown);
    }

    [Fact]
    public async Task A_running_game_blocks_it_and_the_card_says_which()
    {
        var blocked = _preview with { BlockedBy = ErrorInfo.From(new GameRunningException(SampleScenario.SkyrimId, "Skyrim Special Edition")) };
        var rig = await SettingsRig.ShownAsync(WithPreview(blocked));

        await StartAsync(rig);

        var problem = Section(rig).Problem;
        Assert.NotNull(problem);
        Assert.Equal("The game is running", problem.Title);
        Assert.Empty(rig.Dialogs.Shown);
        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.ClearAllDataAsync)));
    }

    [Fact]
    public async Task Declining_in_the_dialog_removes_nothing()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));

        var run = StartAsync(rig);
        Dialog(rig).CancelCommand.Execute(null);
        await run;

        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.ClearAllDataAsync)));
        Assert.False(Section(rig).IsClearing);
    }

    [Fact]
    public async Task Confirming_with_the_typed_word_starts_the_removal()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));

        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;

        Assert.Equal(1, rig.Calls<ISettingsApi>(nameof(ISettingsApi.ClearAllDataAsync)));
        Assert.True(Section(rig).IsClearing);
        Assert.False(Section(rig).RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_removal_that_cannot_start_shows_why()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.ClearAllDataAsync), ErrorInfo.From(new GameRunningException(SampleScenario.SkyrimId, "Skyrim Special Edition")));

        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;

        Assert.False(Section(rig).IsClearing);
        Assert.NotNull(Section(rig).Problem);
    }

    [Fact]
    public async Task When_the_removal_finishes_the_app_returns_to_first_run()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.ClearData).JobId);

        Assert.False(Section(rig).IsClearing);
        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.Empty(rig.Shell.Games);
        Assert.True(rig.Shell.ShowFirstRun);
    }

    [Fact]
    public async Task After_the_removal_the_settings_are_back_to_their_defaults_everywhere()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;

        rig.Backend.Jobs.Complete(rig.Backend.Jobs.LastStarted(JobKind.ClearData).JobId);

        Assert.Equal((ThemePreference.System, AccentColorId.Blue), rig.Theme.Applied.Last());
        Assert.Equal(ThemePreference.System, rig.Screen.Appearance.SelectedTheme!.Preference);
        Assert.Equal(@"C:\Users\you\AppData\Local\SavePoint\Storage", rig.Screen.Storage.StoragePath);
        Assert.Equal(@"C:\Users\you\AppData\Local\SavePoint\Storage", rig.Shell.FirstRun.StoragePath);
    }

    [Fact]
    public async Task A_removal_that_left_files_behind_stays_on_settings_and_lists_them()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;
        var job = rig.Backend.Jobs.LastStarted(JobKind.ClearData);

        rig.Backend.Jobs.Complete(job.JobId, new ClearAllDataResult(70_000_000_000, [new ClearFailure(@"D:\SavePoint\skyrim\objects\pack-1.pack", ErrorInfo.From(new InvalidOperationException("In use")))]));

        Assert.Equal(NavDestination.Settings, rig.Shell.Destination);
        var leftBehind = Section(rig).LeftBehind;
        Assert.NotNull(leftBehind);
        Assert.Contains(@"D:\SavePoint\skyrim\objects\pack-1.pack", leftBehind.Path);
        Assert.Equal("Some files couldn't be removed", leftBehind.Title);
        Assert.False(Section(rig).IsClearing);
    }

    [Fact]
    public async Task Leftovers_in_a_snapshot_folder_are_listed_by_the_game_folder_once_and_never_by_the_git_folder()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;
        var job = rig.Backend.Jobs.LastStarted(JobKind.ClearData);
        var held = ErrorInfo.From(new InvalidOperationException("In use"));

        // Built with the system's own separators, because the folder is found by splitting the path the way the system does.
        var root = Path.Combine(Path.GetTempPath(), "SavePoint");
        var game = Path.Combine(root, "skyrim");
        var catalog = Path.Combine(root, "catalog", "games.json");

        rig.Backend.Jobs.Complete(job.JobId, new ClearAllDataResult(0, [
            new ClearFailure(Path.Combine(game, "install.git", "objects", "pack-1.pack"), held),
            new ClearFailure(Path.Combine(game, "external.git"), held),
            new ClearFailure(catalog, held),
        ]));

        Assert.Equal([game, catalog], Section(rig).LeftBehind!.Path!.Split(Environment.NewLine));
    }

    [Fact]
    public async Task A_failed_removal_shows_why_and_leaves_the_app_where_it_is()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        Confirm(Dialog(rig));
        await run;

        rig.Backend.Jobs.Fail(rig.Backend.Jobs.LastStarted(JobKind.ClearData).JobId, ErrorInfo.From(new InvalidOperationException("Disk error.")));

        Assert.NotNull(Section(rig).Problem);
        Assert.Equal(NavDestination.Settings, rig.Shell.Destination);
        Assert.False(Section(rig).IsClearing);
    }

    [Fact]
    public async Task A_removal_already_running_when_the_screen_shows_is_picked_up()
    {
        var backend = WithPreview(_preview);
        FakeAssert.Ok(await backend.Settings.ClearAllDataAsync());
        var rig = await SettingsRig.StartAsync(backend);

        rig.Show();

        Assert.True(Section(rig).IsClearing);
    }

    [Fact]
    public async Task Starting_again_clears_the_last_problem_and_the_left_behind_card()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        rig.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetClearAllDataPreviewAsync), ErrorInfo.From(new InvalidOperationException("No preview.")));
        await StartAsync(rig);
        Assert.NotNull(Section(rig).Problem);

        var run = StartAsync(rig);

        Assert.Null(Section(rig).Problem);
        Dialog(rig).CancelCommand.Execute(null);
        await run;
    }

    // ---- The dialog

    [Fact]
    public async Task The_dialog_says_what_goes_and_what_stays()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        Assert.Equal("Remove all SavePoint data?", dialog.Title);
        Assert.Equal(
            "Every snapshot and setting is deleted and SavePoint starts over as if new. This can't be undone.",
            dialog.Intro);
        Assert.Equal(
            [
                "3 games and 6 profiles, with all their snapshots",
                @"79.3 GB of snapshots in D:\SavePoint",
                @"SavePoint's settings and game list, in C:\Users\you\AppData\Roaming\SavePoint",
            ],
            dialog.Items);
        Assert.Equal(
            "Your game folders and their contents stay as they are. SavePoint only removes the small file it added to 2 game folders.",
            dialog.Stays);
        Assert.Equal("This frees 79.3 GB.", dialog.Freed);
        dialog.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task With_no_marker_folders_the_dialog_does_not_mention_markers()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview with { ConsentMarkerFolders = [] }));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        Assert.Equal("Your game folders and their contents stay as they are.", dialog.Stays);
        dialog.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task One_game_and_one_profile_read_in_the_singular()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview with { GameCount = 1, ProfileCount = 1, ConsentMarkerFolders = [@"C:\Games\SkyrimSE"] }));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        Assert.Equal("1 game and 1 profile, with all their snapshots", dialog.Items[0]);
        Assert.EndsWith("added to 1 game folder.", dialog.Stays);
        dialog.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task The_dialog_shows_the_word_to_type_and_keeps_confirm_locked_until_it_is_typed()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        Assert.Equal("DELETE", dialog.Word);
        Assert.Equal("Type DELETE to confirm.", dialog.Prompt);
        Assert.False(dialog.CanConfirm);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.TypedText = "DELET";
        Assert.False(dialog.CanConfirm);

        dialog.TypedText = "  delete ";
        Assert.True(dialog.CanConfirm);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
        dialog.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task Confirm_does_nothing_while_the_word_is_not_typed_even_when_run_directly()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        dialog.ConfirmCommand.Execute(null);

        Assert.False(dialog.IsClosed);
        dialog.CancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task Escape_dismisses_the_dialog_as_a_no()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        dialog.RequestDismiss();
        await run;

        Assert.True(dialog.IsClosed);
        Assert.Equal(0, rig.Calls<ISettingsApi>(nameof(ISettingsApi.ClearAllDataAsync)));
    }

    [Fact]
    public async Task The_dialog_is_wide_enough_for_its_list_and_names_its_confirm_button()
    {
        var rig = await SettingsRig.ShownAsync(WithPreview(_preview));
        var run = StartAsync(rig);
        var dialog = Dialog(rig);

        Assert.Equal(520, dialog.DialogWidth);
        Assert.Equal("Remove all data", dialog.ConfirmLabel);
        dialog.CancelCommand.Execute(null);
        await run;
    }
}
