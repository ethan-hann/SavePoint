using Savepoint.App.Features.Help;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Features.History;
using Savepoint.App.Tests.Features.Launch;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// Each explanation sits with the idea it explains: the screens that own one expose it, the dialogs build theirs from what they know.
public class ContextualHelpPlacementTests : IDisposable
{
    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public async Task History_asks_what_a_snapshot_is_and_its_link_opens_the_page()
    {
        var rig = await HistoryRig.ShownAsync();

        Assert.Equal("What's a snapshot?", rig.Screen.SnapshotHelp.Question);
        Assert.True(rig.Screen.SnapshotHelp.HasLink);
        rig.Screen.SnapshotHelp.OpenHowItWorksCommand.Execute(null);
        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
    }

    [Fact]
    public async Task Settings_asks_how_much_space_snapshots_take_and_its_link_opens_the_page()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.Equal("How much space do snapshots take?", rig.Screen.SpaceHelp.Question);
        Assert.True(rig.Screen.SpaceHelp.HasLink);
        rig.Screen.SpaceHelp.OpenHowItWorksCommand.Execute(null);
        Assert.IsType<HowItWorksViewModel>(rig.Shell.Content);
    }

    [Fact]
    public async Task The_launch_check_explains_new_changes_in_each_block_of_changed_files_and_save_snapshots_under_the_saves()
    {
        var dialog = await _rig.DialogAsync();

        Assert.Equal("What's a save snapshot?", dialog.SaveSnapshotHelp.Question);
        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.HasHelp);
        Assert.Equal("What are new changes?", block.Help!.Question);
    }

    [Fact]
    public async Task A_folder_a_switch_left_half_done_holds_a_mix_of_two_snapshots_so_it_gets_no_new_changes_explanation()
    {
        var dialog = await _rig.DialogAsync(change: preview => preview.WithFolder(RepoRole.Install, install => install with { Dirty = true, DirtyOwnerProfileId = null, UnfinishedSwitch = true, UnfinishedSwitchTarget = new LaunchTarget.ProfileTip(SampleScenario.ModdedId) }));

        var unfinished = dialog.UnsavedBlocks.Single(block => block.IsUnfinishedSwitch);
        var ordinary = dialog.UnsavedBlocks.Single(block => !block.IsUnfinishedSwitch);

        Assert.False(unfinished.HasHelp);
        Assert.True(ordinary.HasHelp);
    }

    [Fact]
    public async Task The_dialog_after_playing_from_an_older_snapshot_asks_which_to_pick_and_names_the_profile()
    {
        var exited = await _rig.ExitedAsync(LaunchRig.ClearedBlackreachPoint);

        var dialog = new PostPlayDialogViewModel(exited, "Modded 1.6", _rig.Deps);

        Assert.True(dialog.HasOlderChoiceHelp);
        Assert.Equal("Which should I pick?", dialog.OlderChoiceHelp!.Question);
        Assert.Contains("\"Modded 1.6\"", dialog.OlderChoiceHelp.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_dialog_after_a_normal_session_has_no_older_choice_explanation_but_the_replace_card_has_its_own()
    {
        var exited = await _rig.ExitedAsync(LaunchRig.ModdedTip);

        var dialog = new PostPlayDialogViewModel(exited, "Modded 1.6", _rig.Deps);

        Assert.False(dialog.HasOlderChoiceHelp);
        var replace = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);
        Assert.Equal("When should I replace?", replace.Help!.Question);
        Assert.Contains("\"Before the final boss\"", replace.Help.Answer, StringComparison.Ordinal);
        Assert.All(dialog.Choices.Where(choice => choice.Kind != CompletionChoice.UpdateExisting), choice => Assert.Null(choice.Help));
    }

    [Fact]
    public async Task The_replace_explanation_shows_only_while_its_card_is_selected()
    {
        var dialog = new PostPlayDialogViewModel(await _rig.ExitedAsync(LaunchRig.ModdedTip), "Modded 1.6", _rig.Deps);
        var replace = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);

        Assert.False(replace.ShowHelp);
        dialog.SelectedChoice = replace;
        Assert.True(replace.ShowHelp);
    }

    [Fact]
    public async Task A_profile_row_explains_the_two_states_that_have_no_room_for_a_button()
    {
        using var app = await ComposedApp.StartAsync();

        var rows = app.Games.Profiles;

        Assert.Contains(rows, row => row.IsUnsavedLine);
        Assert.Contains(rows, row => row.IsActiveLine);
        foreach (var row in rows)
        {
            var expected = row.IsUnsavedLine
                ? "Files in your game folders changed since this profile's last snapshot."
                : row.IsActiveLine ? "This profile's files are in your game folders right now." : null;
            Assert.Equal(expected, row.HelpText);
        }
    }
}
