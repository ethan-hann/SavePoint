using Savepoint.App.Features.Common.Dialogs;

namespace Savepoint.App.Tests.Features.Common;

public class DialogHostViewModelTests
{
    [Fact]
    public void A_host_with_no_dialogs_is_closed()
    {
        var host = new DialogHostViewModel();

        Assert.False(host.IsOpen);
        Assert.Null(host.Current);
        Assert.Empty(host.Stack);
    }

    [Fact]
    public void Showing_a_dialog_opens_the_host_with_that_dialog_on_top()
    {
        var host = new DialogHostViewModel();
        var dialog = new SampleDialog("first");

        var pending = host.ShowAsync(dialog);

        Assert.True(host.IsOpen);
        Assert.Same(dialog, host.Current);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task Closing_the_dialog_completes_the_task_and_closes_the_host()
    {
        var host = new DialogHostViewModel();
        var dialog = new SampleDialog("first");
        var pending = host.ShowAsync(dialog);

        dialog.Answer(7);

        Assert.True(pending.IsCompleted);
        Assert.Equal(7, await pending);
        Assert.False(host.IsOpen);
        Assert.Null(host.Current);
    }

    [Fact]
    public void A_second_dialog_covers_the_first_and_closing_it_uncovers_the_first()
    {
        var host = new DialogHostViewModel();
        var first = new SampleDialog("first");
        var second = new SampleDialog("second");
        _ = host.ShowAsync(first);
        _ = host.ShowAsync(second);

        Assert.Same(second, host.Current);
        Assert.Equal([first, second], host.Stack);

        second.Answer(1);

        Assert.Same(first, host.Current);
        Assert.True(host.IsOpen);
    }

    [Fact]
    public void Requesting_a_dismiss_reaches_only_the_dialog_on_top()
    {
        var host = new DialogHostViewModel();
        var first = new SampleDialog("first");
        var second = new SampleDialog("second");
        _ = host.ShowAsync(first);
        _ = host.ShowAsync(second);

        host.RequestDismissCurrent();

        Assert.True(second.IsClosed);
        Assert.False(first.IsClosed);
        Assert.Same(first, host.Current);
    }

    [Fact]
    public void A_dialog_that_refuses_dismissal_stays_open()
    {
        var host = new DialogHostViewModel();
        var dialog = new SampleDialog("busy") { Dismissible = false };
        _ = host.ShowAsync(dialog);

        host.RequestDismissCurrent();

        Assert.True(host.IsOpen);
        Assert.False(dialog.IsClosed);
    }

    [Fact]
    public void Requesting_a_dismiss_with_nothing_open_does_nothing()
    {
        var host = new DialogHostViewModel();

        host.RequestDismissCurrent();

        Assert.False(host.IsOpen);
    }

    [Fact]
    public void Current_and_is_open_notify_when_they_change()
    {
        var host = new DialogHostViewModel();
        var changed = new List<string?>();
        host.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var dialog = new SampleDialog("first");

        _ = host.ShowAsync(dialog);
        dialog.Answer(1);

        Assert.Equal(2, changed.Count(name => name == nameof(DialogHostViewModel.Current)));
        Assert.Equal(2, changed.Count(name => name == nameof(DialogHostViewModel.IsOpen)));
    }

    [Fact]
    public async Task A_dialog_that_is_already_closed_is_not_shown()
    {
        var host = new DialogHostViewModel();
        var dialog = new SampleDialog("done");
        dialog.Answer(3);

        var pending = host.ShowAsync(dialog);

        Assert.True(pending.IsCompleted);
        Assert.Equal(3, await pending);
        Assert.False(host.IsOpen);
    }

    private sealed class SampleDialog(string title) : DialogViewModel<int>(title)
    {
        public bool Dismissible { get; set; } = true;

        public override bool CanDismiss => Dismissible;

        protected override int DismissResult => -1;

        public void Answer(int value) => Close(value);
    }
}
