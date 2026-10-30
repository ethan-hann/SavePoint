namespace Savepoint.App.Tests.Fakes;

// A destructive question nobody answered must not be accepted behind the test's back.
public class FakeDialogsTests
{
    private static readonly Savepoint.App.Features.Common.Dialogs.ConfirmRequest _request = new("Delete Skyrim?", "This cannot be undone.", "Delete game", Destructive: true);

    [Fact]
    public async Task A_confirmation_with_no_answer_queued_fails_the_test_instead_of_being_accepted()
    {
        var dialogs = new FakeDialogs();

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => dialogs.ConfirmAsync(_request));

        Assert.Contains("Delete Skyrim?", failure.Message, StringComparison.Ordinal);
        dialogs.Unanswered.Clear();
    }

    [Fact]
    public async Task A_confirmation_with_no_answer_is_recorded_for_the_end_of_the_test_and_an_answered_one_is_not()
    {
        var dialogs = new FakeDialogs().AnswerConfirm(true);

        Assert.True(await dialogs.ConfirmAsync(_request));
        await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => dialogs.ConfirmAsync(_request));

        Assert.Equal(["Delete Skyrim?"], FailOnUnansweredConfirmationAttribute.Unanswered([dialogs]));
        dialogs.Unanswered.Clear();
        Assert.Empty(FailOnUnansweredConfirmationAttribute.Unanswered([dialogs]));
    }

}
