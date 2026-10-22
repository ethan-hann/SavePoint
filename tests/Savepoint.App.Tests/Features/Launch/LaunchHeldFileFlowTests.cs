using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// A file SavePoint cannot read is the only thing wrong with a folder the launch has to switch: the player is told which file and can
// try again, and is not asked to keep or discard changes nobody made (D37).
public class LaunchHeldFileFlowTests : IDisposable
{
    private const string _file = @"C:\Games\SkyrimSE\Data\Skyrim.esm";

    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    private async Task HoldAsync(ErrorInfo held)
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.ModdedTip)] = source with { ShowBanner = false, Install = source.Install with { MatchesTarget = false, HeldBy = held } };
    }

    [Fact]
    public async Task A_file_in_use_shows_its_card_with_try_again_and_no_dialog()
    {
        await HoldAsync(ErrorInfo.From(new GitOperationException(_file, "in use")));

        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("A file is in use, so the switch didn't start", card.Title);
        Assert.Equal(
            "Something else on your computer is holding one of the game's files open. Close the game, its launcher and any mod manager, then try again. Nothing has been changed.",
            card.Body);
        Assert.Equal(_file, card.Path);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.True(FlowRig.Action(card, "Try again").IsPrimary);
        Assert.Empty(_f.Dialogs.Shown);
        Assert.Empty(_f.Starts());
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_says_so_and_offers_try_again_too()
    {
        await HoldAsync(ErrorInfo.From(new FileUnreadableException(_file)));

        await _f.Play(LaunchRig.ModdedTip);

        var card = _f.Inline<ErrorCardViewModel>();
        Assert.Equal("SavePoint can't read a file", card.Title);
        Assert.Equal(_file, card.Path);
        Assert.Equal(["Try again", "Dismiss"], card.Actions.Select(action => action.Label));
        Assert.Empty(_f.Dialogs.Shown);
    }

    [Fact]
    public async Task Trying_again_reads_the_folders_again_and_goes_on_once_the_file_is_free()
    {
        await HoldAsync(ErrorInfo.From(new GitOperationException(_file, "in use")));
        await _f.Play(LaunchRig.ModdedTip);
        var before = _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count;
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.ModdedTip, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.ModdedTip)] = source with { Install = source.Install with { HeldBy = null } };

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Try again");

        Assert.True(_f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count > before);
        Assert.NotEmpty(_f.Dialogs.Shown);
    }

    // The unsaved work is the first thing to settle: the dialog asks about it, and the held file is met when the switch starts.
    [Fact]
    public async Task A_file_in_use_beside_unsaved_changes_opens_the_dialog_and_not_the_card()
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.SurvivalTip, TestContext.Current.CancellationToken));
        Assert.True(source.External!.Dirty);
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.SurvivalTip)] = source with { Install = source.Install with { HeldBy = ErrorInfo.From(new GitOperationException(_file, "in use")) } };

        var begin = _f.Play(LaunchRig.SurvivalTip);

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.NotEmpty(_f.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }

    [Fact]
    public async Task A_file_in_use_in_a_half_switched_folder_opens_the_dialog_and_not_the_card()
    {
        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.VanillaTip, TestContext.Current.CancellationToken));
        var held = source.Install with { Dirty = true, UnfinishedSwitch = true, HeldBy = ErrorInfo.From(new GitOperationException(_file, "in use")) };
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.VanillaTip)] = source with { Install = held };

        var begin = _f.Play(LaunchRig.VanillaTip);

        Assert.Null(_f.Flow.InlineContentFor(FlowRig.Skyrim));
        Assert.NotEmpty(_f.Dialogs.Shown.OfType<LaunchDialogViewModel>());
        _f.LaunchDialog.CancelCommand.Execute(null);
        await begin;
    }
}
