using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Hosting;

/// <summary>
/// The last window of a crashed app: it says SavePoint has to close, offers the details behind "Show details" and the log folder, and
/// ends with a button that closes it. It is built from plain controls, with none of the app's styles or resources, because the crash
/// may be the very thing that broke them.
/// </summary>
public sealed class CrashWindow : Window
{
    private readonly UiFormatter _text;
    private readonly Button _detailsButton;
    private readonly TextBox _detailsText;

    /// <summary>Creates the window.</summary>
    /// <param name="text">Formats the window's catalog messages.</param>
    /// <param name="report">The crash to show.</param>
    /// <param name="openFolder">Opens a folder in the file manager; answers false when it could not.</param>
    public CrashWindow(UiFormatter text, CrashReport report, Func<string, bool> openFolder)
    {
        _text = text;
        Title = text.Message("app.name");
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var content = new StackPanel { Spacing = 12, Margin = new Thickness(24), Width = 520 };
        content.Children.Add(new TextBlock { Text = text.Message("app.crash.title"), FontSize = 16, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        var body = report.LogFolder is null ? text.Message("app.crash.bodyNoLog") : text.Message("app.crash.body");
        content.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        _detailsButton = NewButton("DetailsButton", text.Message("errorCard.showDetails"));
        _detailsButton.Click += (_, _) => ToggleDetails();
        buttons.Children.Add(_detailsButton);

        if (report.LogFolder is { } folder)
        {
            content.Children.Add(new SelectableTextBlock { Text = text.Message("app.crash.logFolder", ("path", folder)), TextWrapping = TextWrapping.Wrap });
            var failed = new TextBlock { Name = "OpenFailedText", Text = text.Message("app.crash.openFailed"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var open = NewButton("OpenLogFolderButton", text.Message("app.crash.openLogFolder"));
            open.Click += (_, _) => failed.IsVisible = !openFolder(folder);
            buttons.Children.Add(open);
            content.Children.Add(failed);
        }

        _detailsText = new TextBox
        {
            Name = "DetailsText",
            Text = report.Exception.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            MaxHeight = 240,
            IsVisible = false,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_detailsText, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_detailsText, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        content.Children.Add(_detailsText);

        var close = NewButton("CloseButton", text.Message("app.crash.close"));
        close.Click += (_, _) => Close();
        buttons.Children.Add(close);
        content.Children.Add(buttons);
        Content = content;
    }

    private static Button NewButton(string name, string label)
    {
        var button = new Button { Name = name, Content = label };
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void ToggleDetails()
    {
        _detailsText.IsVisible = !_detailsText.IsVisible;
        var label = _detailsText.IsVisible ? _text.Message("errorCard.hideDetails") : _text.Message("errorCard.showDetails");
        _detailsButton.Content = label;
        AutomationProperties.SetName(_detailsButton, label);
    }
}
