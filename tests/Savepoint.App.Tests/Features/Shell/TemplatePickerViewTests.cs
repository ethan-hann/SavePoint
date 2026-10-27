using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Shell;

public class TemplatePickerViewTests
{
    private static async Task<(ComposedApp App, TemplatePickerViewModel Picker)> OpenAsync()
    {
        var app = await ComposedApp.StartWithViewAsync();
        var picker = new TemplatePickerViewModel(app.Backend.Games.Templates, app.Text);
        _ = app.Main.Dialogs.ShowAsync(picker);
        app.Settle();
        return (app, picker);
    }

    [AvaloniaFact]
    public async Task A_search_nothing_matches_offers_a_named_button_to_clear_it_that_Tab_reaches()
    {
        var (app, picker) = await OpenAsync();
        using var _ = app;
        picker.SearchText = "no such game";
        app.Settle();
        var button = app.Find<Button>("ClearSearchButton");

        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Clear search", AutomationAudit.NameOf(button));
        Assert.True(button.Focusable && button.IsTabStop);

        // Tab from the search box passes the (empty) list and lands on the button.
        app.Find<TextBox>("SearchField").Focus();
        app.Press(Key.Tab, PhysicalKey.Tab);
        Assert.Same(button, app.Focused);
    }

    [AvaloniaFact]
    public async Task The_button_clears_the_search_with_the_keyboard_and_focus_goes_back_to_the_search_box()
    {
        var (app, picker) = await OpenAsync();
        using var _ = app;
        picker.SearchText = "no such game";
        app.Settle();
        app.Find<Button>("ClearSearchButton").Focus();

        app.Press(Key.Enter, PhysicalKey.Enter);

        Assert.Equal(string.Empty, picker.SearchText);
        Assert.False(app.Find<StackPanel>("NoMatches").IsEffectivelyVisible);
        Assert.Same(app.Find<TextBox>("SearchField"), app.Focused);
    }
}
