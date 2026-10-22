using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Launch;

// "Try again" on a card from a check before play repeats the check for the profile the player chose last, never the start of an earlier session.
public class LaunchTryAgainTargetTests : IDisposable
{
    private const string _file = @"C:\Games\SkyrimSE\Data\Skyrim.esm";

    private readonly FlowRig _f = new();

    public void Dispose() => _f.Dispose();

    // Plays Modded to its end, so the flow remembers that start, then holds a file for the Vanilla check.
    private async Task FinishModdedThenHoldForVanillaAsync(ErrorInfo held)
    {
        await _f.PlayToExitedAsync();
        _f.PostPlay.NotNowCommand.Execute(null);
        Assert.False(_f.Flow.IsActive(FlowRig.Skyrim));

        var source = FakeAssert.Ok(await _f.Backend.Launch.GetPreviewAsync(FlowRig.Skyrim, LaunchRig.VanillaTip, TestContext.Current.CancellationToken));
        _f.Backend.State.LaunchPreviews[(FlowRig.Skyrim, LaunchRig.VanillaTip)] = source with { Install = source.Install with { HeldBy = held } };
    }

    private int Previews(LaunchTarget target) =>
        _f.Backend.Calls.To<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync)).Count(call => call.Args.Contains(target));

    [Fact]
    public async Task Try_again_on_the_file_card_checks_the_profile_just_chosen_and_starts_nothing_from_the_session_before()
    {
        await FinishModdedThenHoldForVanillaAsync(ErrorInfo.From(new GitOperationException(_file, "in use")));
        await _f.Play(LaunchRig.VanillaTip);
        var startsBefore = _f.Starts().Count;
        var vanillaChecksBefore = Previews(LaunchRig.VanillaTip);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Try again");

        Assert.True(Previews(LaunchRig.VanillaTip) > vanillaChecksBefore);
        Assert.Equal(startsBefore, _f.Starts().Count);
        Assert.DoesNotContain(_f.Starts().Skip(startsBefore), start => start.Args.Contains(LaunchRig.ModdedTip));
    }

    [Fact]
    public async Task Try_again_on_a_check_that_failed_for_any_other_reason_checks_the_profile_just_chosen_too()
    {
        await _f.PlayToExitedAsync();
        _f.PostPlay.NotNowCommand.Execute(null);
        _f.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), ErrorInfo.From(new InvalidOperationException("the scan broke")));
        await _f.Play(LaunchRig.VanillaTip);
        var startsBefore = _f.Starts().Count;
        var vanillaChecksBefore = Previews(LaunchRig.VanillaTip);

        FlowRig.Press(_f.Inline<ErrorCardViewModel>(), "Try again");

        Assert.True(Previews(LaunchRig.VanillaTip) > vanillaChecksBefore);
        Assert.Equal(startsBefore, _f.Starts().Count);
    }
}
