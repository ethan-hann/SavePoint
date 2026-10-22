using Avalonia;
using Savepoint.App.Hosting;
using Savepoint.App.Platform;
using Savepoint.App.Theme.Fonts;
using Velopack;

namespace Savepoint.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The installer and updater start the executable with hook arguments; they must be handled, and may end the process, before the single-instance guard or any window exists.
        VelopackApp.Build().Run();
        return Run(args);
    }

    private static int Run(string[] args)
    {
        // One copy per user (D26): two copies working on the same history would be worse than one that cannot be brought forward.
        using var single = new SingleInstance();
        if (!single.TryAcquire())
        {
            if (single.SignalFirstInstance())
            {
                return 0;
            }

            // The running copy did not answer, so there is no window to bring forward; say so briefly instead of vanishing.
            App.ShowAlreadyRunningNotice = true;
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // A backend that cannot start becomes the startup screen's error card, never a crash (D18).
        var started = AppBootstrap.Start();
        using var closeLog = started.Log as IDisposable;

        // Whatever nothing catches is written to the log first, then shown in a last window, and the process ends after it.
        // Running jobs are asked to stop before the window shows. Asking is not stopping: a job that is slow to unwind can still be changing
        // a game's folders while the window is up.
        var crash = new CrashHandler(
            started.Log,
            new AvaloniaCrashPresenter(path => started.Services.FileRevealer?.Reveal(path) ?? false),
            Environment.Exit,
            started.Services.LogFolder,
            () => RunningJobs.CancelAll(started.Services.Jobs, TimeSpan.FromSeconds(2)));
        using var processHooks = crash.AttachToProcess();
        App.Crash = crash;
        App.Services = started.Services;
        App.SingleInstance = single;
        App.IntegrateWithSystem = true;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the headless test host and the designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSavepointFonts()
            .LogToTrace();
}
