using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Features.Games;
using Savepoint.App.Tests.Features.Launch;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Help;

// The app explains itself at three moments of decision, the first time each comes up for a player, and then keeps out of the way.
public class DecisionPointHelpTests : IDisposable
{
    private const string _hint = "Not sure? Cancel and choose Take a snapshot first. It only costs disk space.";

    private readonly LaunchRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private AppSettings Stored() => FakeAssert.Ok(_rig.Backend.Settings.GetAsync().GetAwaiter().GetResult());

    private int Writes() => _rig.Backend.Calls.To<ISettingsApi>(nameof(ISettingsApi.SaveAsync)).Count;

    private async Task<PostPlayDialogViewModel> OlderAsync(bool explainOlderChoice = false, bool explainReplace = false) =>
        new(await _rig.ExitedAsync(LaunchRig.ClearedBlackreachPoint), "Modded 1.6", _rig.Deps, explainOlderChoice, explainReplace);

    private async Task<PostPlayDialogViewModel> NormalAsync(bool explainReplace) =>
        new(await _rig.ExitedAsync(LaunchRig.ModdedTip), "Modded 1.6", _rig.Deps, explainReplace: explainReplace);

    // ---- The first older-snapshot choice after playing

    [Fact]
    public async Task The_first_time_a_player_meets_the_older_snapshot_choice_the_explanation_is_already_open_and_is_remembered()
    {
        var dialog = await OlderAsync(explainOlderChoice: true);

        Assert.True(dialog.OlderChoiceHelp!.IsOpen);
        Assert.True(Stored().OlderChoiceHelpSeen);
        Assert.False(Stored().ReplaceHelpSeen);
        Assert.False(Stored().DiscardHelpSeen);
    }

    [Fact]
    public async Task After_the_first_time_the_older_snapshot_explanation_starts_collapsed_and_nothing_is_written()
    {
        var dialog = await OlderAsync(explainOlderChoice: false);

        Assert.False(dialog.OlderChoiceHelp!.IsOpen);
        Assert.Equal(0, Writes());
    }

    [Fact]
    public async Task A_session_that_did_not_start_from_an_older_snapshot_does_not_use_up_the_older_snapshot_explanation()
    {
        var dialog = new PostPlayDialogViewModel(await _rig.ExitedAsync(LaunchRig.ModdedTip), "Modded 1.6", _rig.Deps, explainOlderChoice: true);

        Assert.Null(dialog.OlderChoiceHelp);
        Assert.False(Stored().OlderChoiceHelpSeen);
        Assert.Equal(0, Writes());
    }

    [Fact]
    public async Task The_older_snapshot_explanation_draws_the_picture_of_the_choice_small_inside_the_answer()
    {
        var dialog = await OlderAsync();

        Assert.Equal(HelpPicture.Older, dialog.OlderChoiceHelp!.Picture);
        Assert.True(dialog.OlderChoiceHelp.HasPicture);
    }

    // ---- The first Replace

    [Fact]
    public async Task The_first_time_the_replace_card_is_selected_its_explanation_opens_and_is_remembered()
    {
        var dialog = await NormalAsync(explainReplace: true);
        var replace = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);
        Assert.False(replace.Help!.IsOpen);
        Assert.Equal(0, Writes());

        dialog.SelectedChoice = replace;

        Assert.True(replace.Help.IsOpen);
        Assert.True(Stored().ReplaceHelpSeen);
        Assert.False(Stored().OlderChoiceHelpSeen);
    }

    [Fact]
    public async Task Once_opened_for_the_first_Replace_the_explanation_stays_closed_when_the_player_closes_it_and_comes_back_to_the_card()
    {
        var dialog = await NormalAsync(explainReplace: true);
        var replace = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);
        dialog.SelectedChoice = replace;
        replace.Help!.IsOpen = false;

        dialog.SelectedChoice = dialog.Choices.First(choice => choice.Kind == CompletionChoice.CaptureNew);
        dialog.SelectedChoice = replace;

        Assert.False(replace.Help.IsOpen);
    }

    [Fact]
    public async Task After_the_first_Replace_selecting_the_card_leaves_its_explanation_collapsed_and_writes_nothing()
    {
        var dialog = await NormalAsync(explainReplace: false);
        var replace = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);

        dialog.SelectedChoice = replace;

        Assert.False(replace.Help!.IsOpen);
        Assert.Equal(0, Writes());
    }

    // ---- A permanent discard

    private async Task<string> DiscardMessageAsync(LaunchDialogViewModel dialog, UnsavedChangesBlockViewModel block)
    {
        _rig.Dialogs.AnswerConfirm(false);
        await block.DiscardCommand.ExecuteAsync(null);
        return _rig.Dialogs.Confirmations[^1].Message;
    }

    [Fact]
    public async Task The_first_discard_confirmation_adds_one_line_of_advice_and_it_is_remembered()
    {
        var dialog = await _rig.DialogAsync(explainDiscard: true);

        var message = await DiscardMessageAsync(dialog, dialog.UnsavedBlocks[0]);

        Assert.StartsWith("The saves folder", message, StringComparison.Ordinal);
        Assert.Contains("Progress you saved in the game since then is lost.", message, StringComparison.Ordinal);
        Assert.EndsWith($"This can't be undone. {_hint}", message, StringComparison.Ordinal);
        Assert.True(Stored().DiscardHelpSeen);
        Assert.False(Stored().OlderChoiceHelpSeen);
    }

    [Fact]
    public async Task The_advice_is_given_once_so_a_second_discard_confirmation_does_not_repeat_it()
    {
        var dialog = await _rig.DialogAsync(explainDiscard: true);
        var block = dialog.UnsavedBlocks[0];

        var first = await DiscardMessageAsync(dialog, block);
        var second = await DiscardMessageAsync(dialog, block);

        Assert.Contains(_hint, first, StringComparison.Ordinal);
        Assert.DoesNotContain(_hint, second, StringComparison.Ordinal);
        Assert.EndsWith("This can't be undone.", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_the_first_time_a_discard_confirmation_has_no_advice_and_writes_nothing()
    {
        var dialog = await _rig.DialogAsync(explainDiscard: false);

        var message = await DiscardMessageAsync(dialog, dialog.UnsavedBlocks[0]);

        Assert.DoesNotContain("Not sure?", message, StringComparison.Ordinal);
        Assert.Equal(0, Writes());
    }

    [Fact]
    public async Task Changes_that_belong_to_no_profile_are_advised_to_be_kept_as_a_new_profile_because_there_is_no_snapshot_to_take()
    {
        var dialog = await _rig.DialogAsync(
            explainDiscard: true,
            change: preview => preview.WithFolder(RepoRole.External, saves => saves with { DirtyOwnerProfileId = null }));

        var message = await DiscardMessageAsync(dialog, dialog.UnsavedBlocks[0]);

        Assert.EndsWith("This can't be undone. Not sure? Cancel and keep them as a new profile first. It only costs disk space.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Discarding_a_mix_of_two_snapshots_gets_no_advice_and_does_not_use_up_the_first_time()
    {
        var dialog = await _rig.DialogAsync(
            explainDiscard: true,
            change: preview => preview.WithFolder(RepoRole.Install, install => install with { Dirty = true, DirtyOwnerProfileId = null, UnfinishedSwitch = true, UnfinishedSwitchTarget = new LaunchTarget.ProfileTip(SampleScenario.ModdedId) }));

        var message = await DiscardMessageAsync(dialog, dialog.UnsavedBlocks.Single(block => block.IsUnfinishedSwitch));

        Assert.DoesNotContain("Not sure?", message, StringComparison.Ordinal);
        Assert.False(Stored().DiscardHelpSeen);
    }

    // ---- Remembered per player, not per run: the flow reads what the settings say each time it opens a dialog

    private static async Task<PostPlayDialogViewModel> PlayFromOlderAsync(ComposedApp app)
    {
        var goBack = Assert.IsAssignableFrom<IAsyncRelayCommand>(app.History.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Cleared Blackreach").GoBackCommand).ExecuteAsync(null);
        app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
        await goBack.Soon();
        app.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(SampleScenario.SkyrimId, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
        return app.Dialog<PostPlayDialogViewModel>();
    }

    [Fact]
    public async Task The_flow_opens_the_older_snapshot_explanation_for_a_player_who_has_not_seen_it_and_not_again_for_the_next_session()
    {
        using var app = await ComposedApp.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { OlderChoiceHelpSeen = false })));

        var first = await PlayFromOlderAsync(app);
        Assert.True(first.OlderChoiceHelp!.IsOpen);
        first.ChooseNotNow();
        var second = await PlayFromOlderAsync(app);

        Assert.NotSame(first, second);
        Assert.False(second.OlderChoiceHelp!.IsOpen);
    }

    [Fact]
    public async Task A_player_whose_settings_say_it_was_already_explained_never_sees_it_open_by_itself()
    {
        using var app = await ComposedApp.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { OlderChoiceHelpSeen = true, ReplaceHelpSeen = true })));

        var dialog = await PlayFromOlderAsync(app);

        Assert.False(dialog.OlderChoiceHelp!.IsOpen);
    }

    [Fact]
    public async Task The_flow_adds_the_discard_advice_to_the_first_confirmation_a_player_opens_and_not_to_the_next_dialog()
    {
        using var app = await ComposedApp.StartAsync();
        app.Dialogs.AnswerConfirm(false, false);

        // The play command finishes only when the dialog does, so it is awaited after the dialog is cancelled.
        var firstPlay = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        await app.Dialog<LaunchDialogViewModel>().UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);
        var first = app.Dialogs.Confirmations[^1].Message;
        app.Dialog<LaunchDialogViewModel>().CancelCommand.Execute(null);
        await firstPlay.Soon();
        var secondPlay = app.Games.Profiles.Single(row => row.Id == SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
        await app.Dialog<LaunchDialogViewModel>().UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);
        var second = app.Dialogs.Confirmations[^1].Message;
        app.Dialog<LaunchDialogViewModel>().CancelCommand.Execute(null);
        await secondPlay.Soon();

        Assert.Contains(_hint, first, StringComparison.Ordinal);
        Assert.DoesNotContain("Not sure?", second, StringComparison.Ordinal);
    }
}
