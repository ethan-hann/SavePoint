using Savepoint.App.Platform.Notifications;
using Savepoint.App.Platform.Processes;
using Savepoint.App.Tests.Platform.Fakes;

namespace Savepoint.App.Tests.Platform;

public class MacNotificationServiceTests
{
    [Fact]
    public async Task The_notification_goes_to_osascript_as_one_script_argument()
    {
        var runner = new FakeProcessRunner();
        var service = new MacNotificationService(runner);

        await service.ShowAsync("Capture finished", "Skyrim: 2 folders saved");

        var command = Assert.Single(runner.Run);
        Assert.Equal("/usr/bin/osascript", command.FileName);
        Assert.Equal(2, command.Arguments.Count);
        Assert.Equal("-e", command.Arguments[0]);
        var (body, title) = AppleScriptReader.ReadDisplayNotification(command.Arguments[1]);
        Assert.Equal("Capture finished", title);
        Assert.Equal("Skyrim: 2 folders saved", body);
    }

    [Fact]
    public async Task Text_with_quotes_and_backslashes_stays_inside_its_literal_in_the_single_script_argument()
    {
        var runner = new FakeProcessRunner();
        var service = new MacNotificationService(runner);
        const string Title = "He said \"stop\" \\ now";
        const string Body = "\" & (do shell script \"touch x\") & \"\nsecond line";

        await service.ShowAsync(Title, Body);

        var command = Assert.Single(runner.Run);
        Assert.Equal(2, command.Arguments.Count);
        var (body, title) = AppleScriptReader.ReadDisplayNotification(command.Arguments[1]);
        Assert.Equal(Title, title);
        Assert.Equal(Body, body);
    }

    [Fact]
    public void Show_starts_the_notification_without_waiting_for_it()
    {
        var runner = new FakeProcessRunner();
        var service = new MacNotificationService(runner);

        service.Show("Title", "Body");

        Assert.Single(runner.Run);
    }

    [Fact]
    public async Task A_failing_osascript_is_ignored()
    {
        var runner = FakeProcessRunner.Printing("execution error", exitCode: 1);
        var service = new MacNotificationService(runner);

        await service.ShowAsync("Title", "Body");

        Assert.Single(runner.Run);
    }

    [Fact]
    public async Task A_missing_osascript_is_ignored()
    {
        var service = new MacNotificationService(FakeProcessRunner.Unavailable());

        var error = await Record.ExceptionAsync(() => service.ShowAsync("Title", "Body"));

        Assert.Null(error);
    }

    [Fact]
    public async Task A_runner_that_throws_never_reaches_the_caller()
    {
        var service = new MacNotificationService(new ThrowingRunner());

        var error = await Record.ExceptionAsync(() => service.ShowAsync("Title", "Body"));

        Assert.Null(error);
    }

    [Fact]
    public void Show_does_not_throw_when_the_runner_throws()
    {
        var service = new MacNotificationService(new ThrowingRunner());

        var error = Record.Exception(() => service.Show("Title", "Body"));

        Assert.Null(error);
    }

    private sealed class ThrowingRunner : IProcessRunner
    {
        public bool TryStart(ProcessCommand command) => throw new InvalidOperationException("boom");

        public Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }
}
