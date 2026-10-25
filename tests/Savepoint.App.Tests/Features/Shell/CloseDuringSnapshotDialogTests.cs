using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;

namespace Savepoint.App.Tests.Features.Shell;

public class CloseDuringSnapshotDialogTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    [Theory]
    [InlineData(CloseDuringSnapshotChoice.Minimize)]
    [InlineData(CloseDuringSnapshotChoice.CloseAndStop)]
    [InlineData(CloseDuringSnapshotChoice.Stay)]
    public async Task Each_button_answers_with_its_choice(CloseDuringSnapshotChoice choice)
    {
        var dialog = new CloseDuringSnapshotDialogViewModel(_text);

        var command = choice switch
        {
            CloseDuringSnapshotChoice.Minimize => dialog.MinimizeCommand,
            CloseDuringSnapshotChoice.CloseAndStop => dialog.CloseAndStopCommand,
            _ => dialog.StayCommand,
        };
        command.Execute(null);

        Assert.Equal(choice, await dialog.Result);
    }

    [Fact]
    public async Task Escape_and_the_scrim_stay_open()
    {
        var dialog = new CloseDuringSnapshotDialogViewModel(_text);

        dialog.RequestDismiss();

        Assert.Equal(CloseDuringSnapshotChoice.Stay, await dialog.Result);
    }

    [AvaloniaFact]
    public void The_view_has_three_buttons_and_minimize_is_the_one_enter_presses()
    {
        var view = (Control)new ViewLocator().Build(new CloseDuringSnapshotDialogViewModel(_text))!;
        view.DataContext = new CloseDuringSnapshotDialogViewModel(_text);
        var window = new Window { Content = view };
        window.Show();

        var minimize = view.FindControl<Button>("MinimizeButton")!;
        var close = view.FindControl<Button>("CloseButton")!;
        var stay = view.FindControl<Button>("StayButton")!;

        Assert.True(minimize.IsDefault);
        Assert.False(close.IsDefault);
        Assert.Equal("Minimize", minimize.Content);
        Assert.Equal("Close and stop the snapshot", close.Content);
        Assert.Equal("Stay open", stay.Content);
    }
}
