using Savepoint.App.Platform.Processes;

namespace Savepoint.App.Platform.Notifications;

/// <summary>Posts notifications through <c>osascript</c>, which needs no signing or entitlement.</summary>
internal sealed class MacNotificationService : INotificationService
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly IProcessRunner _runner;

    public MacNotificationService(IProcessRunner runner)
    {
        _runner = runner;
    }

    /// <inheritdoc />
    public void Show(string title, string body) => _ = ShowAsync(title, body);

    /// <summary>Posts the notification and completes when <c>osascript</c> has finished; never faults.</summary>
    public async Task ShowAsync(string title, string body)
    {
        try
        {
            // The text rides inside one script argument, so nothing is ever parsed by a shell.
            var script = AppleScript.DisplayNotification(title, body);
            await _runner.RunAsync(new ProcessCommand("/usr/bin/osascript", ["-e", script]), _timeout).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A missed notification must not take the app down.
        }
    }
}
