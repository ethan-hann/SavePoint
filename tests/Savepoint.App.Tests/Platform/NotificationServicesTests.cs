using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Savepoint.App.Platform.Notifications;
using Savepoint.App.Tests.Platform.Fakes;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Platform;

public class NotificationServicesTests
{
    private readonly FakeShellNotifyIcon _shell = new();
    private readonly FakeProcessRunner _runner = new();
    private readonly FakeFreedesktopBus _bus = new();
    private readonly ManualTimeProvider _time = new();

    private NotificationParts Parts(Func<nint>? windowHandle = null) =>
        new(windowHandle ?? (() => 0x1000), _shell, window => window + 1, _runner, _bus, _time);

    [Fact]
    public void Windows_gets_the_notification_area_service()
    {
        var service = NotificationServices.Create(OsKind.Windows, "SavePoint", Parts());

        service.Show("Title", "Body");

        Assert.IsType<WindowsNotificationService>(service);
        Assert.Equal(NotifyIconCommand.Add, _shell.Requests[0].Command);
        Assert.Equal("SavePoint", _shell.Requests[0].Tip);
        Assert.Empty(_runner.Run);
        Assert.Empty(_bus.Sent);
    }

    [Fact]
    public void Linux_gets_the_D_Bus_service_under_the_app_name()
    {
        var service = NotificationServices.Create(OsKind.Linux, "Another Name", Parts());

        service.Show("Title", "Body");

        Assert.IsType<LinuxNotificationService>(service);
        Assert.Equal("Another Name", Assert.Single(_bus.Sent).AppName);
        Assert.Empty(_shell.Requests);
        Assert.Empty(_runner.Run);
    }

    [Fact]
    public void MacOS_gets_the_osascript_service()
    {
        var service = NotificationServices.Create(OsKind.MacOS, "SavePoint", Parts());

        service.Show("Title", "Body");

        Assert.IsType<MacNotificationService>(service);
        Assert.Equal("/usr/bin/osascript", Assert.Single(_runner.Run).FileName);
        Assert.Empty(_shell.Requests);
        Assert.Empty(_bus.Sent);
    }

    [Fact]
    public void The_default_app_name_is_SavePoint()
    {
        Assert.Equal("SavePoint", NotificationServices.DefaultAppName);
    }

    [Fact]
    public void The_Windows_icon_outlives_the_latest_notification_longer_than_a_toast_stays_on_screen()
    {
        Assert.True(NotificationServices.IconLifetime >= TimeSpan.FromSeconds(10));
    }

    [AvaloniaFact]
    public void Closing_the_window_takes_the_icon_away_and_ends_the_service()
    {
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        var service = new WindowsNotificationService(() => 0x1000, _shell, w => w, "SavePoint", TimeSpan.FromSeconds(10), _time);
        NotificationServices.AttachToWindow(window, service);
        service.Show("Title", "Body");

        window.Close();
        service.Show("After", "close");

        Assert.Equal(0, _shell.IconCount);
        Assert.Equal(1, _shell.Requests.Count(request => request.Command == NotifyIconCommand.Delete));
        Assert.Equal(1, _shell.Requests.Count(request => request.Command == NotifyIconCommand.Add));
    }

    [AvaloniaFact]
    public void A_close_that_is_canceled_removes_the_icon_but_leaves_the_service_working()
    {
        var window = new Window { Width = 200, Height = 100 };
        window.Show();
        var service = new WindowsNotificationService(() => 0x1000, _shell, w => w, "SavePoint", TimeSpan.FromSeconds(10), _time);
        NotificationServices.AttachToWindow(window, service);
        window.Closing += (_, e) => e.Cancel = true;
        service.Show("Title", "Body");

        window.Close();
        service.Show("Again", "after the canceled close");

        Assert.Equal(1, _shell.IconCount);
        Assert.Equal(2, _shell.Requests.Count(request => request.Command == NotifyIconCommand.Add));
    }

    [Fact]
    public void Only_a_Win32_window_handle_is_taken()
    {
        Assert.Equal(0x4321, WindowHandleSource.HandleOf(new FakePlatformHandle(0x4321, "HWND")));
    }

    [Theory]
    [InlineData("XID")]
    [InlineData("NSWindow")]
    [InlineData("STUB")]
    [InlineData("")]
    public void The_handle_of_any_other_kind_of_window_is_not_a_notification_target(string descriptor)
    {
        Assert.Equal(0, WindowHandleSource.HandleOf(new FakePlatformHandle(0x4321, descriptor)));
    }

    [Fact]
    public void A_window_without_a_handle_gives_zero()
    {
        Assert.Equal(0, WindowHandleSource.HandleOf(null));
        Assert.Equal(0, WindowHandleSource.HandleOf(new FakePlatformHandle(0, "HWND")));
    }

    [AvaloniaFact]
    public void A_headless_window_has_no_Win32_handle_to_offer()
    {
        var window = new Window { Width = 200, Height = 100 };
        window.Show();

        var source = new WindowHandleSource(window);

        Assert.Equal(0, source.Handle);
    }

    private sealed class FakePlatformHandle : IPlatformHandle
    {
        public FakePlatformHandle(nint handle, string descriptor)
        {
            Handle = handle;
            HandleDescriptor = descriptor;
        }

        public nint Handle { get; }

        public string? HandleDescriptor { get; }
    }
}
