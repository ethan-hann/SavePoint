using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Help;
using Savepoint.App.Tests.Fakes;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorModsFolderViewTests
{
    private static Task<EditorViewHarness> StartAsync() =>
        EditorViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithGame(ModsSamples.SevenDays)));

    private static double Top(EditorViewHarness harness, Control control) =>
        control.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;

    [AvaloniaFact]
    public async Task The_mods_section_comes_after_the_install_and_the_saves_sections()
    {
        using var harness = await StartAsync();
        harness.Edit(ModsSamples.Id);
        harness.Settle();

        var install = Top(harness, harness.Find<TextBox>("InstallField"));
        var saves = Top(harness, harness.Find<TextBox>("SavesField"));
        var mods = Top(harness, harness.Find<TextBox>("ModsField"));

        Assert.True(install < saves && saves < mods, $"Expected install < saves < mods, got {install}, {saves}, {mods}.");
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
        Assert.Equal("Mods outside the game folder", AutomationProperties.GetName(harness.Find<TextBox>("ModsField")));
        Assert.Equal("Browse for the mods folder", AutomationProperties.GetName(harness.Find<Button>("BrowseModsButton")));
    }

    [AvaloniaFact]
    public async Task An_empty_mods_field_shows_no_strip_but_a_question_button_that_opens_the_explanation()
    {
        using var harness = await StartAsync();
        harness.New();
        harness.Settle();

        Assert.DoesNotContain(harness.All<Border>(), border => border.Name == "ModsEmptyNote");
        var toggle = harness.Find<HelpQuestionView>("ModsQuestion").GetVisualDescendants().OfType<ToggleButton>().Single();
        Assert.True(toggle.IsEffectivelyVisible);
        Assert.Equal("Does my game keep mods outside its folder?", AutomationProperties.GetName(toggle));
        var answer = harness.Find<HelpAnswerView>("ModsAnswer").GetVisualDescendants().OfType<Border>().Single(border => border.Name == "AnswerBox");
        Assert.False(answer.IsEffectivelyVisible);

        toggle.IsChecked = true;
        harness.Settle();

        Assert.True(answer.IsEffectivelyVisible);
        Assert.Contains(answer.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is { } shown && shown.StartsWith("Most games load mods from inside their own folder", StringComparison.Ordinal));
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
    public async Task A_template_mods_folder_that_is_not_there_yet_shows_the_note_under_the_empty_mods_field()
    {
        using var harness = await EditorViewHarness.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithMissingFolder(ModsSamples.ModsRaw)));
        var editor = harness.New(ModsSamples.SevenDays);
        await editor.LoadAsync();
        harness.Settle();

        Assert.Equal(string.Empty, harness.Find<TextBox>("ModsField").Text);
        var line = harness.Find<Grid>("ModsLeftOutLine");
        Assert.True(line.IsEffectivelyVisible);
        Assert.Contains(line.GetVisualDescendants().OfType<TextBlock>(), text => text.Text is { } shown && shown.EndsWith("so SavePoint left the mods folder out. Once you have mods there, add it in Edit game.", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Each_set_of_common_locations_names_its_own_field_so_a_screen_reader_can_tell_the_two_rows_apart()
    {
        using var harness = await StartAsync();
        harness.New();
        harness.Settle();

        string[] NamesIn(string row) => [.. harness.Find<Grid>(row).GetVisualDescendants().OfType<Button>().Select(button => AutomationProperties.GetName(button) ?? string.Empty)];

        Assert.Equal(
            ["Documents, for the mods folder", "Saved Games, for the mods folder", "AppData, for the mods folder", "Local AppData, for the mods folder"],
            NamesIn("ModsLocationRow"));
        Assert.Equal(
            ["Documents, for the saves folder", "Saved Games, for the saves folder", "AppData, for the saves folder", "Local AppData, for the saves folder"],
            NamesIn("LocationRow"));
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
