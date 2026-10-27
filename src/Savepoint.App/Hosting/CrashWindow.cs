using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Platform;

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
    private readonly Func<string, Task> _copyText;
    private TextBlock? _copyNote;

    /// <summary>Creates the window.</summary>
    /// <param name="text">Formats the window's catalog messages.</param>
    /// <param name="report">The crash to show.</param>
    /// <param name="openFolder">Opens a folder in the file manager; answers false when it could not. It may throw or fault.</param>
    /// <param name="copyText">Puts text on the clipboard; the window's own clipboard when null. It may throw or fault.</param>
    /// <param name="build">The build to state in the support text; this app's build when null.</param>
    public CrashWindow(UiFormatter text, CrashReport report, Func<string, Task<bool>> openFolder, Func<string, Task>? copyText = null, string? build = null)
    {
        _text = text;
        _copyText = copyText ?? CopyToOwnClipboard;
        var supportText = CrashSupportText.Build(report, build ?? ThisBuild());
        Title = text.Message("app.name");
        TryUseAppIcon();
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

        var copy = NewButton("CopyDetailsButton", text.Message("errorCard.copyDetails"));
        copy.Click += async (_, _) => await CopyAsync(supportText);
        buttons.Children.Add(copy);
        _copyNote = new TextBlock { Name = "CopyNoteText", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        content.Children.Add(_copyNote);

        if (report.LogFolder is { } folder)
        {
            content.Children.Add(new SelectableTextBlock { Text = text.Message("app.crash.logFolder", ("path", folder)), TextWrapping = TextWrapping.Wrap });
            var failed = new TextBlock { Name = "OpenFailedText", Text = text.Message("app.crash.openFailed"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var open = NewButton("OpenLogFolderButton", text.Message("app.crash.openLogFolder"));
            open.Click += async (_, _) => failed.IsVisible = !await OpenedAsync(openFolder, folder);
            buttons.Children.Add(open);
            content.Children.Add(failed);
        }

        _detailsText = new TextBox
        {
            Name = "DetailsText",
            Text = supportText,
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

    // A missing icon only costs the generic one; it must never stop the crash from being shown.
    private void TryUseAppIcon()
    {
        try
        {
            Icon = AppIcon.Load();
        }
        catch (Exception)
        {
            // Left as the desktop's default.
        }
    }

    // The build is only a line in the support text; not knowing it must not stop the crash from being shown.
    private static string ThisBuild()
    {
        try
        {
            return Features.Settings.SettingsEnvironment.ThisAppBuild();
        }
        catch (Exception)
        {
            return "unknown build";
        }
    }

    private Task CopyToOwnClipboard(string text) =>
        Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : throw new InvalidOperationException("This window has no clipboard.");

    // A file manager that throws must never cause a second crash; it counts as one that did not open.
    private static async Task<bool> OpenedAsync(Func<string, Task<bool>> openFolder, string folder)
    {
        try
        {
            return await openFolder(folder);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }

    // A clipboard held by another program or missing from the display server must never cause a second crash.
    private async Task CopyAsync(string supportText)
    {
        var message = "errorCard.copied";
        try
        {
            await _copyText(supportText);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            message = "errorCard.copyFailed";
        }

        if (_copyNote is { } note)
        {
            note.Text = _text.Message(message);
            note.IsVisible = true;
        }
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
