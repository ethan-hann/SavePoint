using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.Common;

public class ConfirmDialogViewModelTests
{
    private static ConfirmDialogViewModel Plain(bool destructive = false, string? cancelLabel = null) =>
        new(new ConfirmRequest("Delete Skyrim?", "This can't be undone.", "Delete game", destructive, cancelLabel), "Cancel");

    private static ConfirmDialogViewModel Typed() =>
        new(new ConfirmRequest("Clear all data?", "Everything goes.", "Clear everything", true, Typed: new TypedConfirmation("clear all data", "Type clear all data to continue.")), "Cancel");

    [Fact]
    public void The_dialog_shows_what_the_request_says()
    {
        var dialog = Plain(destructive: true);

        Assert.Equal("Delete Skyrim?", dialog.Title);
        Assert.Equal("This can't be undone.", dialog.Message);
        Assert.Equal("Delete game", dialog.ConfirmLabel);
        Assert.True(dialog.IsDestructive);
        Assert.False(dialog.RequiresTypedWord);
    }

    [Fact]
    public async Task Confirming_answers_true_and_closes()
    {
        var dialog = Plain();

        dialog.ConfirmCommand.Execute(null);

        Assert.True(dialog.IsClosed);
        Assert.True(dialog.Result.IsCompleted);
        Assert.True(await dialog.Result);
    }

    [Fact]
    public async Task Cancelling_answers_false_and_closes()
    {
        var dialog = Plain();

        dialog.CancelCommand.Execute(null);

        Assert.True(dialog.IsClosed);
        Assert.True(dialog.Result.IsCompleted);
        Assert.False(await dialog.Result);
    }

    [Fact]
    public async Task Dismissing_with_escape_or_the_scrim_answers_false()
    {
        var dialog = Plain(destructive: true);

        dialog.RequestDismiss();

        Assert.True(dialog.IsClosed);
        Assert.True(dialog.Result.IsCompleted);
        Assert.False(await dialog.Result);
    }

    [Fact]
    public void The_cancel_label_defaults_to_the_one_given_and_the_request_can_override_it()
    {
        Assert.Equal("Cancel", Plain().CancelLabel);
        Assert.Equal("Keep it", Plain(cancelLabel: "Keep it").CancelLabel);
    }

    [Fact]
    public void Without_a_typed_word_confirming_is_always_possible()
    {
        var dialog = Plain(destructive: true);

        Assert.True(dialog.CanConfirm);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void A_typed_word_keeps_confirm_locked_until_it_is_typed()
    {
        var dialog = Typed();

        Assert.True(dialog.RequiresTypedWord);
        Assert.False(dialog.CanConfirm);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.ConfirmCommand.Execute(null);

        Assert.False(dialog.IsClosed);
    }

    [Theory]
    [InlineData("clear all data")]
    [InlineData("CLEAR ALL DATA")]
    [InlineData("  clear all data  ")]
    public async Task Typing_the_word_unlocks_confirm_ignoring_case_and_surrounding_spaces(string typed)
    {
        var dialog = Typed();

        dialog.TypedText = typed;

        Assert.True(dialog.CanConfirm);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
        dialog.ConfirmCommand.Execute(null);
        Assert.True(dialog.Result.IsCompleted);
        Assert.True(await dialog.Result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("clear all")]
    [InlineData("clear all datum")]
    public void Typing_something_else_keeps_confirm_locked(string typed)
    {
        var dialog = Typed();

        dialog.TypedText = typed;

        Assert.False(dialog.CanConfirm);
    }

    [Fact]
    public void Changing_the_typed_text_tells_the_confirm_command_to_re_evaluate()
    {
        var dialog = Typed();
        var raised = 0;
        dialog.ConfirmCommand.CanExecuteChanged += (_, _) => raised++;

        dialog.TypedText = "clear all data";
        dialog.TypedText = "clear";

        Assert.Equal(2, raised);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    public void Enter_confirms_only_when_the_action_is_not_destructive(bool destructive, bool typed, bool expected)
    {
        var dialog = typed ? Typed() : Plain(destructive);

        Assert.Equal(expected, dialog.ConfirmIsDefault);
    }

    [Fact]
    public void Enter_confirms_a_destructive_action_that_asks_for_a_typed_word()
    {
        Assert.True(Typed().ConfirmIsDefault);
    }
}
