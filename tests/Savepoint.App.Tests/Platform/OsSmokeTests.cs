using System.Runtime.InteropServices;
using Savepoint.App.Platform;
using Savepoint.App.Platform.Motion;
using Savepoint.App.Platform.Notifications;
using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Tests.Platform;

// One opt-in test per OS that makes the real calls the others fake; run by hand with SAVEPOINT_OS_SMOKE=1, since it shows a real notification.
public class OsSmokeTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("SAVEPOINT_OS_SMOKE") == "1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Windows_shows_a_real_balloon_and_removes_its_icon()
    {
        Assert.SkipUnless(Enabled && OperatingSystem.IsWindows(), "Set SAVEPOINT_OS_SMOKE=1 on Windows: this shows a real notification.");
        var window = CreateWindowExW(0, "STATIC", "SavePoint smoke test", 0, 0, 0, 0, 0, -3, 0, 0, 0);
        Assert.NotEqual(0, window);
        var shell = new RecordingShell(new NativeShellNotifyIcon());
        try
        {
            var service = new WindowsNotificationService(() => window, shell, WindowIcons.For, "SavePoint", TimeSpan.FromSeconds(8), TimeProvider.System);

            service.Show("SavePoint smoke test", "This notification comes from the opt-in OS smoke test.");
            await Task.Delay(TimeSpan.FromSeconds(6), Ct);
            service.Dispose();
        }
        finally
        {
            DestroyWindow(window);
        }

        Assert.Equal(
            [(NotifyIconCommand.Add, true), (NotifyIconCommand.Modify, true), (NotifyIconCommand.Delete, true)],
            shell.Results);
        Assert.NotNull(WindowsAnimationSetting.Read());
        await ReducedMotionSource.CreateForCurrentOs().RefreshAsync(Ct);
        Assert.True(RevealTemporaryFile());
    }

    [Fact]
    public async Task Linux_posts_to_the_session_bus_or_quietly_skips_when_there_is_none()
    {
        Assert.SkipUnless(Enabled && OperatingSystem.IsLinux(), "Set SAVEPOINT_OS_SMOKE=1 on Linux: this posts a real notification when a notification service is running.");
        var service = new LinuxNotificationService(DBusNotificationBus.ForCurrentSession(), "SavePoint", TimeSpan.FromSeconds(5));

        var error = await Record.ExceptionAsync(() => service.ShowAsync("SavePoint smoke test", "This notification comes from the opt-in OS smoke test."));

        Assert.Null(error);
        await ReducedMotionSource.CreateForCurrentOs().RefreshAsync(Ct);
        // xdg-open may not be installed, so only a crash would be a failure here.
        RevealTemporaryFile();
    }

    [Fact]
    public async Task MacOS_posts_a_real_notification_through_osascript()
    {
        Assert.SkipUnless(Enabled && OperatingSystem.IsMacOS(), "Set SAVEPOINT_OS_SMOKE=1 on macOS: this shows a real notification.");
        var runner = new RecordingRunner(new SystemProcessRunner());
        var service = new MacNotificationService(runner);

        await service.ShowAsync("SavePoint smoke test", "This notification comes from the opt-in OS smoke test, with \"quotes\" and a \\ backslash.");

        Assert.Equal(0, runner.ExitCodes.Single());
        await ReducedMotionSource.CreateForCurrentOs().RefreshAsync(Ct);
        Assert.True(RevealTemporaryFile());
    }

    // Opens the file manager on a file, which leaves a window on the screen; the file is made in the temp folder and left there.
    private static bool RevealTemporaryFile()
    {
        var file = Path.Combine(Path.GetTempPath(), "savepoint smoke test, with spaces.txt");
        File.WriteAllText(file, "SavePoint smoke test");
        return FileManager.CreateForCurrentOs().Reveal(file);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    // Passes everything through to the real shell and remembers what it answered.
    private sealed class RecordingShell : IShellNotifyIcon
    {
        private readonly IShellNotifyIcon _real;

        public RecordingShell(IShellNotifyIcon real)
        {
            _real = real;
        }

        public List<(NotifyIconCommand Command, bool Accepted)> Results { get; } = [];

        public bool Send(NotifyIconRequest request)
        {
            var accepted = _real.Send(request);
            Results.Add((request.Command, accepted));
            return accepted;
        }
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly IProcessRunner _real;

        public RecordingRunner(IProcessRunner real)
        {
            _real = real;
        }

        public List<int> ExitCodes { get; } = [];

        public bool TryStart(ProcessCommand command) => _real.TryStart(command);

        public async Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var output = await _real.RunAsync(command, timeout, cancellationToken);
            ExitCodes.Add(output?.ExitCode ?? -1);
            return output;
        }
    }
}
