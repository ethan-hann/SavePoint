using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorModsFolderViewTests
{
    private static Task<EditorViewHarness> StartAsync() =>
        EditorViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithGame(ModsSamples.SevenDays)));

    private static double Top(EditorViewHarness harness, Control control) =>
        control.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;

    [AvaloniaFact]
    public async Task The_mods_section_sits_between_the_install_and_the_saves_sections()
    {
        using var harness = await StartAsync();
        harness.Edit(ModsSamples.Id);
        harness.Settle();

        var install = Top(harness, harness.Find<TextBox>("InstallField"));
        var mods = Top(harness, harness.Find<TextBox>("ModsField"));
        var saves = Top(harness, harness.Find<TextBox>("SavesField"));

        Assert.True(install < mods && mods < saves, $"Expected install < mods < saves, got {install}, {mods}, {saves}.");
    }

    [AvaloniaFact]
    public async Task The_mods_field_shows_the_mods_path_with_its_confirmation_and_full_location()
    {
        using var harness = await StartAsync();
        var editor = harness.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        harness.Settle();

        Assert.Equal(ModsSamples.ModsRaw, harness.Find<TextBox>("ModsField").Text);
        Assert.True(harness.Find<StackPanel>("ModsConfirmedLine").IsEffectivelyVisible);
        Assert.True(harness.Find<Grid>("ModsResolvesLine").IsEffectivelyVisible);
        Assert.False(harness.Find<Border>("ModsEmptyNote").IsEffectivelyVisible);
        Assert.Equal("Where the mods live", AutomationProperties.GetName(harness.Find<TextBox>("ModsField")));
        Assert.Equal("Browse for the mods folder", AutomationProperties.GetName(harness.Find<Button>("BrowseModsButton")));
    }

    [AvaloniaFact]
    public async Task An_empty_mods_field_shows_the_strip_that_says_most_games_keep_mods_in_the_game_folder()
    {
        using var harness = await StartAsync();
        var editor = harness.New();
        harness.Settle();

        var note = harness.Find<Border>("ModsEmptyNote");
        Assert.True(note.IsEffectivelyVisible);
        Assert.Contains(note.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is { } shown && shown.StartsWith("No mods folder is set. Most games keep mods inside the game folder", StringComparison.Ordinal));

        editor.ModsPath = @"{appData}\Mods";
        harness.Settle();

        Assert.False(note.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_common_locations_under_the_mods_field_prefix_the_mods_field_only()
    {
        using var harness = await StartAsync();
        var editor = harness.New();
        editor.SavesPath = @"{documents}\Saves";
        harness.Settle();

        var buttons = harness.Find<Grid>("ModsLocationRow").GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["Documents", "Saved Games", "AppData", "Local AppData"], buttons.Select(button => button.Content));

        buttons[2].Command!.Execute(null);
        harness.Settle();

        Assert.Equal("{appData}", harness.Find<TextBox>("ModsField").Text);
        Assert.Equal(@"{documents}\Saves", harness.Find<TextBox>("SavesField").Text);
    }

    [AvaloniaFact]
    public async Task A_mods_folder_error_card_shows_under_the_mods_field()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithGame(ModsSamples.SevenDays).WithFolderStatus(
            ModsSamples.Id,
            new Savepoint.Core.Api.FolderStatus(Savepoint.Core.Catalog.RepoRole.Install, ModsSamples.InstallPath, ModsSamples.InstallPath, true, new Savepoint.Core.Api.ConsentStatus(true, null)),
            new Savepoint.Core.Api.FolderStatus(Savepoint.Core.Catalog.RepoRole.Mods, ModsSamples.ModsRaw, ModsSamples.ModsResolved, false, new Savepoint.Core.Api.ConsentStatus(true, null))));
        using var harness = await EditorViewHarness.StartAsync(backend);
        var editor = harness.Edit(ModsSamples.Id);
        await editor.LoadAsync();
        harness.Settle();

        Assert.True(harness.Find<ContentControl>("ModsCardHost").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("SavesCardHost").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("InstallCardHost").IsEffectivelyVisible);
    }
}
