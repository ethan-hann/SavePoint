using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Help;

// The page in the window: the ways into it (F1, Settings, the first-run link), what it shows, and the ways out.
public class HowItWorksViewTests
{
    private static Button? ButtonNamed(ComposedApp app, string name) => app.All<Button>().FirstOrDefault(button => button.Name == name);

    private static List<string> VisibleTexts(ComposedApp app) =>
        [.. app.All<TextBlock>().Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text)).Select(block => block.Text!)];

    [Fact]
    public void F1_and_Cmd_question_mark_ask_for_help_and_nothing_else_does()
    {
        Assert.True(HelpKey.Matches(Key.F1, KeyModifiers.None));
        Assert.True(HelpKey.Matches(Key.OemQuestion, KeyModifiers.Meta | KeyModifiers.Shift));
        Assert.False(HelpKey.Matches(Key.F1, KeyModifiers.Control));
        Assert.False(HelpKey.Matches(Key.F1, KeyModifiers.Shift));
        Assert.False(HelpKey.Matches(Key.OemQuestion, KeyModifiers.Shift));
        Assert.False(HelpKey.Matches(Key.OemQuestion, KeyModifiers.Meta));
        Assert.False(HelpKey.Matches(Key.F2, KeyModifiers.None));
    }

    [AvaloniaTheory]
    [InlineData(NavDestination.Games)]
    [InlineData(NavDestination.History)]
    [InlineData(NavDestination.Activity)]
    [InlineData(NavDestination.Settings)]
    public async Task F1_opens_the_page_from_every_screen(NavDestination destination)
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(destination);
        app.Settle();

        app.Press(Key.F1, PhysicalKey.F1);

        Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
    }

    [AvaloniaFact]
    public async Task F1_does_nothing_while_a_dialog_is_open()
    {
        using var app = await AppStates.OpenAsync("launch-dialog");

        app.Press(Key.F1, PhysicalKey.F1);

        Assert.IsNotType<HowItWorksViewModel>(app.Shell.Content);
        Assert.True(app.Main.Dialogs.IsOpen);
    }

    [AvaloniaFact]
    public async Task F1_over_the_game_editor_shows_the_page_and_back_gives_the_editor_back_with_its_form_as_it_was()
    {
        using var app = await AppStates.OpenAsync("editor-new");
        var editor = Assert.IsType<GameEditorViewModel>(app.Shell.Content);
        editor.Name = "Half typed";

        app.Press(Key.F1, PhysicalKey.F1);
        Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
        ButtonNamed(app, "BackButton")!.Command!.Execute(null);
        app.Settle();

        Assert.Same(editor, app.Shell.Content);
        Assert.Equal("Half typed", editor.Name);
    }

    [AvaloniaFact]
    public async Task The_page_shows_every_topic_and_a_picture_for_each_but_the_last()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        app.Shell.ShowHelp();
        app.Settle();
        var page = Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
        var texts = VisibleTexts(app);

        Assert.Contains(page.Title, texts);
        Assert.Contains(page.Intro, texts);
        foreach (var topic in page.Topics)
        {
            Assert.Contains(topic.Title, texts);
            Assert.Contains(topic.Body, texts);
        }

        Assert.All(page.Topics.Last().Items, item => Assert.Contains(item, texts));
        Assert.All(page.Topics.Last().Notes, note => Assert.Contains(note, texts));
        var pictures = app.All<HelpPictureView>().Where(picture => picture.IsEffectivelyVisible).ToList();
        Assert.Equal(page.Topics.Count(topic => topic.HasPicture), pictures.Count);
        Assert.All(pictures, picture =>
        {
            Assert.NotNull(picture.Content);
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(picture));
        });
    }

    [AvaloniaFact]
    public async Task The_back_arrow_is_named_Back_takes_focus_when_the_page_opens_and_closes_it()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var screen = app.Shell.CurrentScreen;
        app.Shell.ShowHelp();
        app.Settle();

        var back = ButtonNamed(app, "BackButton");

        Assert.NotNull(back);
        Assert.Equal("Back", AutomationProperties.GetName(back));
        Assert.Same(back, app.Focused);
        back.Command!.Execute(null);
        Assert.Same(screen, app.Shell.Content);
    }

    [AvaloniaFact]
    public async Task Escape_closes_the_page()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var screen = app.Shell.CurrentScreen;
        app.Shell.ShowHelp();
        app.Settle();

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.Same(screen, app.Shell.Content);
    }

    [AvaloniaFact]
    public async Task The_first_run_screen_links_to_the_page()
    {
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings)));

        var link = ButtonNamed(app, "HowItWorksLink");

        Assert.NotNull(link);
        Assert.True(link.IsEffectivelyVisible);
        Assert.Equal("How SavePoint works", AutomationProperties.GetName(link));
        link.Command!.Execute(null);
        Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
    }

    [AvaloniaFact]
    public async Task Settings_opens_with_a_Help_section_whose_button_opens_the_page()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        var sections = app.Find<StackPanel>("Sections");
        var button = ButtonNamed(app, "HowItWorksButton");

        Assert.Equal("HelpSection", Assert.IsType<StackPanel>(sections.Children[0]).Name);
        Assert.NotNull(button);
        Assert.Equal("How SavePoint works", button.Content);
        button.Command!.Execute(null);
        Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
    }
}
