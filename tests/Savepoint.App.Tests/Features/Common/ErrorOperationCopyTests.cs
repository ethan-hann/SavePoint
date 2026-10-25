using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Common;

// A file held open and a full disk can come from several operations; each one's card says its own.
public class ErrorOperationCopyTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static ErrorCardViewModel HeldFile(ErrorOperation operation) => new(SampleScenario.FileInUseError, _text, operation: operation);

    private static ErrorCardViewModel NoRoom(ErrorOperation operation) => new(SampleScenario.SavesOutOfRoomError, _text, operation: operation);

    public static TheoryData<ErrorOperation> Operations => new(Enum.GetValues<ErrorOperation>());

    public static TheoryData<ErrorOperation> OtherThanSwitch => new(Enum.GetValues<ErrorOperation>().Where(operation => operation != ErrorOperation.Switch));

    [Theory]
    [MemberData(nameof(OtherThanSwitch))]
    public void A_held_file_outside_a_switch_never_talks_about_a_switch_or_picking_up(ErrorOperation operation)
    {
        var card = HeldFile(operation);

        Assert.DoesNotContain("switch", card.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("switch", card.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("picks up where it left off", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_file_during_a_switch_says_the_switch_stopped_and_will_pick_up()
    {
        var card = HeldFile(ErrorOperation.Switch);

        Assert.Equal("A file is in use, so the switch stopped partway", card.Title);
        Assert.Contains("picks up where it left off", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_file_during_a_snapshot_says_the_snapshot_did_not_finish_and_the_earlier_ones_are_safe()
    {
        var card = HeldFile(ErrorOperation.Snapshot);

        Assert.Contains("snapshot", card.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arlier snapshots are untouched", card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_file_while_freeing_up_space_names_that_job()
    {
        var card = HeldFile(ErrorOperation.FreeUpSpace);

        Assert.Contains("freeing up space", card.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("freeing up space", card.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_held_leftover_after_a_move_says_the_snapshots_moved_and_an_old_copy_stayed()
    {
        var card = HeldFile(ErrorOperation.MoveSnapshots);

        Assert.Contains("old", card.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("moved", card.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_held_leftover_while_clearing_says_the_rest_was_removed()
    {
        var card = HeldFile(ErrorOperation.ClearData);

        Assert.Contains("couldn't be removed", card.Title, StringComparison.Ordinal);
        Assert.Contains("everything else was removed", card.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_held_file_with_no_known_operation_names_none()
    {
        var card = HeldFile(ErrorOperation.Unspecified);

        Assert.Equal("A file is in use", card.Title);
        Assert.StartsWith("Something else on your computer is holding one of the files open.", card.Body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void Every_held_file_card_has_its_own_nonblank_words(ErrorOperation operation)
    {
        var card = HeldFile(operation);

        Assert.False(string.IsNullOrWhiteSpace(card.Title));
        Assert.False(string.IsNullOrWhiteSpace(card.Body));
    }

    [Theory]
    [InlineData(ErrorOperation.Switch, "to switch")]
    [InlineData(ErrorOperation.Snapshot, "to take this snapshot")]
    [InlineData(ErrorOperation.FreeUpSpace, "to free up space")]
    [InlineData(ErrorOperation.MoveSnapshots, "to move your snapshots")]
    public void A_full_disk_names_the_operation_that_needed_the_room(ErrorOperation operation, string phrase)
    {
        Assert.Contains(phrase, NoRoom(operation).Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ErrorOperation.Switch)]
    [InlineData(ErrorOperation.FreeUpSpace)]
    [InlineData(ErrorOperation.MoveSnapshots)]
    [InlineData(ErrorOperation.ClearData)]
    [InlineData(ErrorOperation.Unspecified)]
    public void A_full_disk_outside_a_snapshot_never_says_to_take_this_snapshot(ErrorOperation operation)
    {
        Assert.DoesNotContain("take this snapshot", NoRoom(operation).Body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void A_full_disk_keeps_telling_the_player_how_much_to_free_whatever_the_operation(ErrorOperation operation)
    {
        Assert.Contains("Free up about 1.2 GB on drive", NoRoom(operation).Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(JobKind.Launch, ErrorOperation.Switch)]
    [InlineData(JobKind.Switch, ErrorOperation.Switch)]
    [InlineData(JobKind.Capture, ErrorOperation.Snapshot)]
    [InlineData(JobKind.Compact, ErrorOperation.FreeUpSpace)]
    [InlineData(JobKind.RelocateStorage, ErrorOperation.MoveSnapshots)]
    [InlineData(JobKind.ClearData, ErrorOperation.ClearData)]
    public void Each_kind_of_job_maps_to_its_operation(JobKind kind, ErrorOperation expected)
    {
        Assert.Equal(expected, ErrorOperations.Of(kind));
    }

    // A new kind of job has to be given an operation on purpose, not fall through to the neutral words.
    [Theory]
    [MemberData(nameof(JobKinds))]
    public void No_kind_of_job_falls_back_to_the_neutral_operation(JobKind kind)
    {
        Assert.NotEqual(ErrorOperation.Unspecified, ErrorOperations.Of(kind));
    }

    public static TheoryData<JobKind> JobKinds => new(Enum.GetValues<JobKind>());
}
