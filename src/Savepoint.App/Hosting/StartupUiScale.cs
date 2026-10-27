using Savepoint.App.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Hosting;

/// <summary>Reads the saved interface size before the main window is built, so the window opens at its final size and place.</summary>
public static class StartupUiScale
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(2);

    /// <summary>The saved interface size as a step, or 100 when the settings cannot be read in a couple of seconds.</summary>
    /// <param name="settings">The settings Api.</param>
    public static int Read(ISettingsApi settings) => Read(() => settings.GetAsync(), _wait);

    internal static int Read(Func<Task<OperationResult<AppSettings>>> read, TimeSpan wait)
    {
        try
        {
            // Run on the thread pool so the wait cannot deadlock on the UI thread's context.
            var reading = Task.Run(read);
            return reading.Wait(wait) && reading.Result is { Ok: true } stored ? UiScale.Nearest(stored.Value.UiScalePercent) : UiScale.Default;
        }
        catch (AggregateException)
        {
            return UiScale.Default;
        }
    }
}
