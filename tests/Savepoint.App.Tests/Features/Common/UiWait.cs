using Avalonia.Threading;

namespace Savepoint.App.Tests.Features.Common;

// Waits for work that finishes on another thread and reports back through the UI thread, such as an image being decoded.
internal static class UiWait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int milliseconds = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }

            // A plain test resumes on a pool thread, where running the UI thread's queue throws; its work needs no pumping there.
            if (Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.RunJobs();
            }

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        return true;
    }
}
