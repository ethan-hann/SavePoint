using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Help;

// The help is for everyone: a screen reader finds its headings and hears what opens, and nothing on it is a hard target to hit.
public class HelpAccessTests
{
    private const double _smallest = 24;

    [AvaloniaFact]
    public async Task The_page_title_is_a_level_one_heading_and_each_topic_title_a_level_two_heading()
    {
        using var app = await AppStates.OpenAsync("how-it-works");
        var page = Assert.IsType<HowItWorksViewModel>(app.Shell.Content);

        var title = app.All<TextBlock>().FirstOrDefault(block => block.Name == "TitleText");
        var topics = app.Find<ScrollViewer>("PageScroll").GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && block.FindAncestorOfType<HelpPictureView>() is null && page.Topics.Any(topic => topic.Title == block.Text))
            .ToList();

        Assert.NotNull(title);
        Assert.Equal(1, AutomationProperties.GetHeadingLevel(title));
        Assert.Equal(page.Topics.Count, topics.Count);
        Assert.All(topics, topic => Assert.Equal(2, AutomationProperties.GetHeadingLevel(topic)));
    }

    [AvaloniaFact]
    public async Task The_older_choice_explanation_that_opens_by_itself_is_spoken_when_the_dialog_appears()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var announcer = app.All<AnnouncementRegion>().First(region => region.Name == "PageAnnouncer");
        var spoken = new List<string>();
        ControlAutomationPeer.CreatePeerForElement(announcer).PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty && e.NewValue is string { Length: > 0 } name)
            {
                spoken.Add(name);
            }
        };

        await app.PlayFromOlderSnapshotAsync();
        app.Settle();

        var dialog = app.Dialog<PostPlayDialogViewModel>();
        Assert.True(dialog.OlderChoiceHelp!.IsOpen);
        Assert.Contains(dialog.OlderChoiceHelp.Answer, spoken);
    }

    [AvaloniaFact]
    public async Task The_first_discard_confirmation_shows_its_advice_in_the_window_and_Escape_closes_only_the_confirmation()
    {
        using var app = await AppStates.OpenAsync("launch-discard-first-time");

        var texts = app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? string.Empty).ToList();

        Assert.Contains(texts, text => text.EndsWith("This can't be undone. Not sure? Cancel and choose Take a snapshot first. It only costs disk space.", StringComparison.Ordinal));
        Assert.Equal(2, app.Main.Dialogs.Stack.Count);
        app.Press(Avalonia.Input.Key.Escape, Avalonia.Input.PhysicalKey.Escape);
        Assert.Single(app.Main.Dialogs.Stack);
        Assert.IsType<LaunchDialogViewModel>(app.Main.Dialogs.Current);
    }

    [AvaloniaTheory]
    [InlineData(660)]
    [InlineData(760)]
    public async Task Settings_opens_at_the_top_so_its_Help_section_is_in_view(int height)
    {
        using var app = await ComposedApp.StartWithViewAsync(height: height);
        app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        var scroll = app.Find<ScrollViewer>("PageScroll");
        var help = LayoutAudit.BoundsIn(scroll, app.Find<Control>("HelpSection"));

        Assert.Equal(0, scroll.Offset.Y);
        Assert.True(help.Top >= 0, $"The Help section starts {-help.Top:0}px above the top of the page.");
    }

    public static TheoryData<string> StatesWithHelpButtons() =>
    [
        "history-help-open",
        "settings-help-open",
        "launch-dialog-help-open",
        "post-play-replace-help-open",
        "post-play-from-older-snapshot",
        "tour-step-2",
        "tour-step-4",
        "how-it-works",
        "settings",
    ];

    [AvaloniaTheory]
    [MemberData(nameof(StatesWithHelpButtons))]
    public async Task Every_button_the_help_adds_is_at_least_24_by_24_pixels(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        var names = new[] { "MoreButton", "TourSkipButton", "TourBackButton", "TourNextButton", "TourButton", "HowItWorksButton" };
        var buttons = app.All<ToggleButton>().Where(toggle => toggle.Classes.Contains("help-link")).Cast<Control>()
            .Concat(app.All<Button>().Where(button => button.Name is { } name && names.Contains(name)).Cast<Control>())
            .Where(control => control.IsEffectivelyVisible)
            .ToList();

        Assert.NotEmpty(buttons);
        var small = buttons.Where(button => button.Bounds.Width < _smallest || button.Bounds.Height < _smallest).Select(button => $"{AutomationProperties.GetName(button)} is {button.Bounds.Width:0}x{button.Bounds.Height:0}").ToList();
        Assert.True(small.Count == 0, $"{state}: {string.Join("; ", small)}");
    }

    [AvaloniaTheory]
    [InlineData("history-help-open", 640)]
    [InlineData("history-help-open", 800)]
    [InlineData("settings-help-open", 640)]
    [InlineData("settings-help-open", 800)]
    public async Task An_open_explanation_on_a_screen_reflows_inside_the_window_at_narrow_widths(string state, int width)
    {
        using var app = await AppStates.OpenAsync(state);
        app.Window.Width = width;
        app.Settle();
        app.Render("reflow", $"{state}-{width}");

        var answers = app.All<Border>().Where(border => border.Name == "AnswerBox" && border.IsEffectivelyVisible).ToList();

        Assert.NotEmpty(answers);
        Assert.All(answers, answer => Assert.True(LayoutAudit.BoundsIn(app.Window, answer).Right <= app.Window.Bounds.Width, $"The answer ends at {LayoutAudit.BoundsIn(app.Window, answer).Right:0} in a {width}px window."));
        var problems = LayoutAudit.Problems(app.Window);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }
}
