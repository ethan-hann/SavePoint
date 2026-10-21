using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Hosting;

/// <summary>
/// The brief notice a second copy shows when the copy that is running could not be brought forward: it says SavePoint is
/// already open, and closes itself after a few seconds. Two copies working on the same history would be worse than none, so
/// the second never carries on.
/// </summary>
public sealed class AlreadyRunningWindow : Window
{
    /// <summary>How long the notice stays before it closes by itself.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(8);

    private readonly DispatcherTimer _timer;

    /// <summary>Creates the notice.</summary>
    /// <param name="text">Formats the notice's catalog messages.</param>
    public AlreadyRunningWindow(UiFormatter text)
    {
        Title = text.Message("app.name");
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var heading = new TextBlock { Text = text.Message("app.alreadyRunning.title"), FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { Text = text.Message("app.alreadyRunning.body"), TextWrapping = TextWrapping.Wrap };
        var ok = new Button { Content = text.Message("app.alreadyRunning.ok"), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(ok, text.Message("app.alreadyRunning.ok"));
        ok.Click += (_, _) => Close();
        Content = new StackPanel { Spacing = 12, Margin = new Thickness(24), MaxWidth = 380, Children = { heading, body, ok } };

        _timer = new DispatcherTimer { Interval = Lifetime };
        _timer.Tick += (_, _) => Close();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }
}
