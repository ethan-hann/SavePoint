using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.Common;

public class DialogViewModelTests
{
    [Fact]
    public async Task Closing_completes_the_result_with_the_answer_and_raises_closed_once()
    {
        var dialog = new SampleDialog();
        var closed = 0;
        dialog.Closed += (_, _) => closed++;

        dialog.Answer("chosen");

        Assert.True(dialog.Result.IsCompleted);
        Assert.True(dialog.Result.IsCompleted);
        Assert.Equal("chosen", await dialog.Result);
        Assert.True(dialog.IsClosed);
        Assert.Equal(1, closed);
    }

    [Fact]
    public async Task Closing_twice_keeps_the_first_answer()
    {
        var dialog = new SampleDialog();
        var closed = 0;
        dialog.Closed += (_, _) => closed++;

        dialog.Answer("first");
        dialog.Answer("second");

        Assert.True(dialog.Result.IsCompleted);
        Assert.Equal("first", await dialog.Result);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void An_open_dialog_has_no_result_yet()
    {
        var dialog = new SampleDialog();

        Assert.False(dialog.Result.IsCompleted);
        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public async Task Dismissing_closes_with_the_dismiss_result_when_allowed()
    {
        var dialog = new SampleDialog();

        dialog.RequestDismiss();

        Assert.True(dialog.IsClosed);
        Assert.True(dialog.Result.IsCompleted);
        Assert.Equal("dismissed", await dialog.Result);
    }

    [Fact]
    public void Dismissing_does_nothing_while_the_dialog_cannot_be_dismissed()
    {
        var dialog = new SampleDialog { Dismissible = false };

        dialog.RequestDismiss();

        Assert.False(dialog.IsClosed);
        Assert.False(dialog.Result.IsCompleted);
    }

    [Fact]
    public void A_dialog_carries_its_title_and_default_width()
    {
        var dialog = new SampleDialog();

        Assert.Equal("Sample", dialog.Title);
        Assert.Equal(440, dialog.DialogWidth);
    }

    private sealed class SampleDialog : DialogViewModel<string>
    {
        public SampleDialog()
            : base("Sample")
        {
        }

        public bool Dismissible { get; set; } = true;

        public override bool CanDismiss => Dismissible;

        protected override string DismissResult => "dismissed";

        public void Answer(string value) => Close(value);
    }
}
