using Savepoint.App.Features.Common.Errors;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// A switch that stopped part way: the card and the dialog finish it by the target the folder was heading for, or discard it (D31).
public class LaunchUnfinishedSwitchFlowTests : IDisposable
{
    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    // The launch of the cleared-Blackreach point fails with the unfinished-switch error, and the folder now reports the state the real backend would.
    private async Task FailLaunchWithUnfinishedSwitchAsync(LaunchTarget? recorded, string? errorTarget = "0123456789abcdef0123456789abcdef01234567")
    {
        await _f.PlayWithProgressAsync();
        var key = (FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint);
        var preview = _f.Backend.State.LaunchPreviews[key];
        _f.Backend.State.LaunchPreviews[key] = preview with
        {
            Install = preview.Install with { Dirty = true, DirtyFileCount = 4, DirtyOwnerProfileId = null, UnfinishedSwitch = true, UnfinishedSwitchTarget = recorded },
        };
        _f.Backend.Launch.FailLaunch(FlowRig.Skyrim, ErrorInfo.From(new InterruptedSwitchException(RepoRole.Install, errorTarget)));
    }

    // A note SavePoint could not read: nothing knows which switch it was, so nothing can finish it and discarding is the way out.
    [Fact]
    public async Task When_the_switch_is_not_known_the_card_offers_the_review_first_and_does_not_offer_to_finish_it()
    {
        await FailLaunchWithUnfinishedSwitchAsync(null, errorTarget: null);
        var card = _f.Inline<ErrorCardViewModel>();

        Assert.Equal(["Review the changes", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Review the changes").IsPrimary);
        Assert.DoesNotContain("Finish the switch", card.Body, StringComparison.Ordinal);
        Assert.Contains("discard the mix", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_the_switch_is_known_the_card_says_it_can_be_finished()
    {
        await FailLaunchWithUnfinishedSwitchAsync(LaunchRig.ModdedTip);

        Assert.EndsWith("Finish the switch, or discard the mix.", _f.Inline<ErrorCardViewModel>().Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_the_switch_is_not_known_the_review_offers_discarding_only_and_discarding_frees_the_switch()
    {
        await FailLaunchWithUnfinishedSwitchAsync(null, errorTarget: null);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Review the changes");

        var dialog = _f.LaunchDialog;
        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.IsUnfinishedSwitch);
        Assert.False(block.CanSaveFirst);
        Assert.False(block.CanFinishSwitch);
        Assert.False(block.FinishSwitchCommand.CanExecute(null));
        Assert.DoesNotContain("Finish the switch", block.Body, StringComparison.Ordinal);
        Assert.Contains("discard the mix", block.Body, StringComparison.OrdinalIgnoreCase);
        Assert.True(block.DiscardCommand.CanExecute(null));

        _f.Dialogs.AnswerConfirm(true);
        await block.DiscardCommand.ExecuteAsync(null);

        var call = Assert.Single(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.DiscardChangesAsync)));
        Assert.Equal([FlowRig.Skyrim, RepoRole.Install], call.Args);
        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.CanSwitch);
    }

    [Fact]
    public async Task The_card_offers_switch_again_first_and_the_review_second()
    {
        await FailLaunchWithUnfinishedSwitchAsync(LaunchRig.ModdedTip);
        var card = _f.Inline<ErrorCardViewModel>();

        Assert.Equal(["Finish the switch", "Review the changes", "Dismiss"], card.Actions.Select(a => a.Label));
        Assert.True(FlowRig.Action(card, "Finish the switch").IsPrimary);
        Assert.False(FlowRig.Action(card, "Review the changes").IsPrimary);
    }

    [Fact]
    public async Task Switch_again_starts_the_target_the_unfinished_switch_was_heading_for()
    {
        await FailLaunchWithUnfinishedSwitchAsync(LaunchRig.ModdedTip);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Finish the switch");

        Assert.Equal(2, _f.Starts().Count);
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip], _f.Starts()[1].Args);
    }

    [Fact]
    public async Task Without_a_recorded_target_switch_again_opens_the_review_instead_of_repeating_the_refused_launch()
    {
        await FailLaunchWithUnfinishedSwitchAsync(null);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Finish the switch");

        Assert.Single(_f.Starts());
        Assert.Equal("Skyrim Special Edition", _f.LaunchDialog.GameName);
    }

    [Fact]
    public async Task Review_the_changes_shows_the_unfinished_switch_with_finish_and_discard_and_no_keep_offer()
    {
        await FailLaunchWithUnfinishedSwitchAsync(LaunchRig.ModdedTip);

        var previewsBefore = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Review the changes");

        // The review reads the folders again for the point that was being switched to, and shows what it finds.
        var previews = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync));
        Assert.True(previews.Count > previewsBefore);
        Assert.Equal([FlowRig.Skyrim, LaunchRig.ClearedBlackreachPoint], previews[^1].Args.Take(2));
        var dialog = _f.LaunchDialog;
        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.True(block.IsUnfinishedSwitch);
        Assert.Equal("The last switch didn't finish", block.Title);
        Assert.False(block.CanSaveFirst);
        Assert.False(dialog.ShowKeepCallout);
        Assert.True(block.FinishSwitchCommand.CanExecute(null));
        Assert.True(block.DiscardCommand.CanExecute(null));
    }

    [Fact]
    public async Task Finishing_the_last_switch_from_the_dialog_starts_the_recorded_target()
    {
        await FailLaunchWithUnfinishedSwitchAsync(LaunchRig.ModdedTip);
        var review = FlowRig.AsyncCommand(FlowRig.Action(_f.Inline<ErrorCardViewModel>(), "Review the changes")).ExecuteAsync(null);

        _f.LaunchDialog.UnsavedBlocks[0].FinishSwitchCommand.Execute(null);
        await review;

        Assert.Equal([FlowRig.Skyrim, LaunchRig.ModdedTip], _f.Starts()[^1].Args);
    }
}
