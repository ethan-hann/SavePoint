using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Savepoint.App.Tests.Features.Common;

public class UiWaitTests
{
    // A plain test resumes on a pool thread after an await, and work other tests left on the UI thread's queue must not run there.
    [AvaloniaFact]
    public async Task Waiting_on_a_pool_thread_leaves_work_posted_to_the_UI_thread_for_the_UI_thread()
    {
        var ranOn = 0;
        Dispatcher.UIThread.Post(() => ranOn = Environment.CurrentManagedThreadId, DispatcherPriority.Send);
        var ready = false;
        _ = Task.Delay(100, TestContext.Current.CancellationToken).ContinueWith(_ => ready = true, TaskScheduler.Default);

        var failure = await Record.ExceptionAsync(() => Task.Run(() => UiWait.UntilAsync(() => ready), TestContext.Current.CancellationToken));

        Assert.Null(failure);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
    }
}
