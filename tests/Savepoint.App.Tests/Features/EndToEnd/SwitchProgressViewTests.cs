using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.EndToEnd;

public class SwitchProgressViewTests
{
    [AvaloniaFact]
    public async Task The_button_that_ends_a_switch_says_it_stops_it_and_what_that_leaves()
    {
        using var app = await AppStates.OpenAsync("launch-progress");

        var button = app.Find<Button>("CancelButton");

        Assert.Equal("Stop switching", button.Content);
        var note = app.Find<TextBlock>("StopNoteText");
        Assert.True(note.IsEffectivelyVisible);
        Assert.Equal("Stopping leaves the game's folder with a mix of two snapshots. Press Play again and SavePoint finishes the job.", note.Text);
    }
}
