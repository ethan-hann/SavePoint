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
    }

    [Fact]
    public async Task A_confirmation_takes_the_answers_queued_in_order()
    {
        var dialogs = new FakeDialogs().AnswerConfirm(false, true);

        Assert.False(await dialogs.ConfirmAsync(_request));
        Assert.True(await dialogs.ConfirmAsync(_request));
    }

    [Fact]
    public async Task A_default_answer_stands_in_for_the_confirmations_nobody_queued()
    {
        var dialogs = new FakeDialogs { DefaultConfirmAnswer = false };

        Assert.False(await dialogs.ConfirmAsync(_request));
    }
}
