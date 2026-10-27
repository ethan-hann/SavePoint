using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// What the flow says and does when a game has a mods folder beside its game and saves folders.
public class LaunchFlowModsFolderTests : IDisposable
{
    private const string _modsPath = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods";
    private const string _file = @"C:\Users\you\AppData\Roaming\7DaysToDie\Mods\Cool Mod\mod.xml";

    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private Guid CaptureJob() => _f.Backend.Jobs.LastStarted(JobKind.Capture).JobId;

    private async Task CompleteWithAsync(params FolderOutcome[] folders)
    {
        await _f.PlayToExitedAsync();
        await _f.PostPlay.SaveCommand.ExecuteAsync(null);
        _f.Backend.Jobs.Complete(CaptureJob(), new CaptureResult(SampleScenario.ModdedId, SampleScenario.BeforeFinalBoss, folders));
    }

    private async Task ScriptAsync(LaunchTarget target, Func<LaunchPreview, LaunchPreview> change)
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, target, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, target)] = change(source);
    }

    [Fact]
    public async Task A_save_that_took_the_game_and_saves_folders_but_not_the_mods_says_which_part_is_missing_and_offers_to_retry_it()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.External));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("Only part of the snapshot was taken", card.Title);
        Assert.StartsWith("Your game version is in the snapshot. Your saves are in the snapshot. Your mods aren't yet. Not enough free disk space", card.Body, StringComparison.Ordinal);
        var rows = Assert.IsType<FolderOutcomeListViewModel>(card.Supplement).Rows;
        Assert.Equal(["Game version · in the snapshot", "Mods · not in the snapshot yet", "Saves · in the snapshot"], rows.Select(r => r.Text));
        Assert.Equal(["Retry the mods", "Dismiss"], card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task A_save_that_failed_in_two_folders_offers_one_retry_for_what_did_not_save()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.External, SampleScenario.SavesOutOfRoomError));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your game version is in the snapshot. Your mods aren't yet. Your saves aren't yet.", card.Body, StringComparison.Ordinal);
        Assert.Equal(["Retry what's missing", "Dismiss"], card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task A_game_version_that_did_not_save_beside_a_mods_folder_does_not_claim_the_mods_with_it()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install, SampleScenario.SavesOutOfRoomError), new FolderOutcome(RepoRole.Mods), new FolderOutcome(RepoRole.External));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your mods are in the snapshot. Your saves are in the snapshot. Your game version isn't yet.", card.Body, StringComparison.Ordinal);
        Assert.Equal(["Retry the game version", "Dismiss"], card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task When_nothing_saved_the_card_names_the_game_version_alone_beside_a_mods_folder()
    {
        await CompleteWithAsync(
            new FolderOutcome(RepoRole.Install, SampleScenario.SavesOutOfRoomError),
            new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError),
            new FolderOutcome(RepoRole.External, null, LeftAlone: true, LeftOnBranch: "profile/other"));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your game version isn't in the snapshot yet. Your mods aren't in the snapshot yet.", card.Body, StringComparison.Ordinal);
        Assert.Contains("Your saves weren't included, because you played them from another profile's snapshot", card.Body, StringComparison.Ordinal);
        var rows = Assert.IsType<FolderOutcomeListViewModel>(card.Supplement).Rows;
        Assert.Equal(["Game version · not in the snapshot yet", "Mods · not in the snapshot yet", "Saves · not included"], rows.Select(r => r.Text));
    }

    [Fact]
    public async Task A_failed_mods_folder_beside_a_folder_the_save_left_alone_names_the_mods_as_the_one_that_failed()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install, null, LeftAlone: true, LeftOnBranch: "profile/other"), new FolderOutcome(RepoRole.Mods, SampleScenario.SavesOutOfRoomError));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your mods aren't in the snapshot yet. Not enough free disk space", card.Body, StringComparison.Ordinal);
        Assert.Contains("Your game version wasn't included, because you played it from another profile's snapshot", card.Body, StringComparison.Ordinal);
        Assert.Equal(["Retry the mods", "Dismiss"], card.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task A_game_version_replacement_that_became_a_new_snapshot_beside_a_mods_folder_does_not_claim_the_mods()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install, KeptAsNew: true), new FolderOutcome(RepoRole.Mods), new FolderOutcome(RepoRole.External));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your game version didn't end with the snapshot you chose to replace", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_game_version_the_save_left_alone_beside_a_mods_folder_does_not_claim_the_mods()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install, null, LeftAlone: true, LeftOnBranch: "profile/other"), new FolderOutcome(RepoRole.Mods), new FolderOutcome(RepoRole.External));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your game version wasn't included, because you played it from another profile's snapshot.", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mods_folder_the_save_left_alone_is_named_as_your_mods()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, null, LeftAlone: true, LeftOnBranch: "profile/other"));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your mods weren't included, because you played them from another profile's snapshot.", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mods_replacement_that_became_a_new_snapshot_says_your_mods_did_not_end_with_the_snapshot_you_chose()
    {
        await CompleteWithAsync(new FolderOutcome(RepoRole.Install), new FolderOutcome(RepoRole.Mods, KeptAsNew: true));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.StartsWith("Your mods didn't end with the snapshot you chose to replace", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_file_in_use_in_the_mods_folder_shows_the_card_with_the_file_and_no_dialog()
    {
        await ScriptAsync(LaunchRig.ModdedTip, p => (p with { ShowBanner = false })
            .WithFolder(new FolderLaunchState(RepoRole.Mods, _modsPath, false, false, 0, null, null, HeldBy: ErrorInfo.From(new GitOperationException(_file, "in use")))));

        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("A file is in use, so the switch didn't start", card.Title);
        Assert.Equal(_file, card.Path);
        Assert.Empty(_f.Dialogs.Shown);
    }

    [Fact]
    public async Task Unsaved_changes_in_the_mods_folder_still_ask_even_when_only_the_saves_differ_and_the_player_skips_that_dialog()
    {
        FakeAssert.Ok(await _f.Backend.Settings.SaveAsync(SampleScenario.Settings with { SkipConfirmWhenOnlySaveChanges = true }));
        await ScriptAsync(LaunchRig.ClearedBlackreachPoint, p => p.WithFolder(new FolderLaunchState(RepoRole.Mods, _modsPath, true, true, 3, SampleScenario.ModdedId, null)));

        var begin = _f.Play(LaunchRig.ClearedBlackreachPoint);

        Assert.Empty(_f.Starts());
        var dialog = _f.LaunchDialog;
        Assert.Equal([RepoRole.Mods], dialog.UnsavedBlocks.Select(b => b.Role));
        dialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task A_switch_stopped_in_the_mods_folder_is_finished_by_the_target_the_mods_folder_records()
    {
        await _f.PlayWithProgressAsync();
        var unfinished = new FolderLaunchState(RepoRole.Mods, _modsPath, false, true, 4, null, null, UnfinishedSwitch: true, UnfinishedSwitchTarget: LaunchRig.ModdedTip);
        var key = (FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint);
        _f.Backend.State.LaunchPreviews[key] = _f.Backend.State.LaunchPreviews[key].WithFolder(unfinished);
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new InterruptedSwitchException(RepoRole.Mods, "0123456789abcdef0123456789abcdef01234567")));

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Contains("mods folder has files from two snapshots", card.Body, StringComparison.Ordinal);
        FlowRig.Press(card, "Finish the switch");

        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip], _f.Starts()[1].Args);
    }

    [Fact]
    public async Task The_banner_checks_list_the_mods_folder_with_its_path_when_the_game_has_one()
    {
        var preview = await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken);
        var withMods = FakeAssert.Ok(preview).WithFolder(new FolderLaunchState(RepoRole.Mods, _modsPath, true, false, 0, null, null));

        var banner = new LaunchBannerViewModel(withMods, "Skyrim Special Edition", "Modded", _f.Rig.Text, _f.Dialogs);

        Assert.Equal(
            ["Game version: already on 1.6.640 with 212 mod files", "Mods: already match this profile", "Saves: already match \"Before the final boss\"", "No new changes in your game files, mods or saves"],
            banner.Checks.Select(c => c.Text));
        Assert.Equal(_modsPath, banner.Checks[1].Path);
    }

    [Fact]
    public async Task The_banner_check_of_a_game_version_it_cannot_name_does_not_claim_the_mods_when_the_game_has_a_mods_folder()
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken));
        var preview = (source with { TargetInstall = new InstallState(null, null) }).WithFolder(new FolderLaunchState(RepoRole.Mods, _modsPath, true, false, 0, null, null));

        var banner = new LaunchBannerViewModel(preview, "Skyrim Special Edition", "Modded", _f.Rig.Text, _f.Dialogs);

        Assert.Equal("Game version: already match this profile", banner.Checks[0].Text);
    }

    [Fact]
    public async Task A_game_with_a_mods_folder_and_no_saves_folder_says_game_files_and_mods_in_its_last_check()
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken));
        var preview = (source with { AvailableSaves = [], SelectedSaveId = null })
            .WithoutFolder(RepoRole.External)
            .WithFolder(new FolderLaunchState(RepoRole.Mods, _modsPath, true, false, 0, null, null));

        var banner = new LaunchBannerViewModel(preview, "Skyrim Special Edition", "Modded", _f.Rig.Text, _f.Dialogs);

        Assert.Equal("No new changes in your game files or mods", banner.Checks[^1].Text);
    }
}
