using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Accessibility;
using Savepoint.App.Features.Help;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.Help;

// What a player meets: a short link that opens its answer in place, by keyboard as well as mouse, closes with Escape and is announced.
public class ContextualHelpViewTests
{
    private static ToggleButton? Question(ComposedApp app, ContextualHelpViewModel help) =>
        app.All<HelpQuestionView>().Where(view => ReferenceEquals(view.DataContext, help)).Select(view => view.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault()).FirstOrDefault();

    private static Border? Answer(ComposedApp app, ContextualHelpViewModel help) =>
        app.All<HelpAnswerView>().Where(view => ReferenceEquals(view.DataContext, help)).Select(view => view.GetVisualDescendants().OfType<Border>().FirstOrDefault(border => border.Name == "AnswerBox")).FirstOrDefault();

    private static List<string> Spoken(ComposedApp app, Action act)
    {
        var announcer = app.All<AnnouncementRegion>().First(region => region.Name == "PageAnnouncer");
        var events = new List<string>();
        ControlAutomationPeer.CreatePeerForElement(announcer).PropertyChanged += (_, e) =>
        {
            if (e.Property == AutomationElementIdentifiers.NameProperty && e.NewValue is string { Length: > 0 } name)
            {
                events.Add(name);
            }
        };
        act();
        app.Settle();
        return events;
    }

    private static async Task<(ComposedApp App, ContextualHelpViewModel Help)> HistoryAsync()
    {
        var app = await ComposedApp.StartWithViewAsync();
        var history = app.Show<HistoryScreenViewModel>(NavDestination.History);
        return (app, history.SnapshotHelp);
    }

    [AvaloniaFact]
    public async Task The_History_header_has_a_collapsed_question_that_opens_its_answer_below_the_header()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;

        var button = Question(app, help);
        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("What's a snapshot?", AutomationProperties.GetName(button));
        Assert.False(Answer(app, help)?.IsEffectivelyVisible ?? false);

        button.IsChecked = true;
        app.Settle();

        var answer = Answer(app, help);
        Assert.NotNull(answer);
        Assert.True(answer.IsEffectivelyVisible);
        Assert.Contains(answer.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == help.Answer);
        var header = app.Find<Border>("Header");
        Assert.True(LayoutAudit.BoundsIn(app.Window, answer).Top >= LayoutAudit.BoundsIn(app.Window, header).Bottom - 1, "The answer opens over the header instead of below it.");
        Assert.True(help.IsOpen);
    }

    [AvaloniaFact]
    public async Task The_answer_ends_with_a_link_to_the_page_on_screens()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;
        help.IsOpen = true;
        app.Settle();

        var more = Answer(app, help)!.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "MoreButton");

        Assert.NotNull(more);
        Assert.True(more.IsEffectivelyVisible);
        Assert.Equal("More in How SavePoint works", AutomationProperties.GetName(more));
        more.Command!.Execute(null);
        Assert.IsType<HowItWorksViewModel>(app.Shell.Content);
    }

    [AvaloniaFact]
    public async Task The_question_is_a_toggle_that_tells_assistive_technology_whether_the_answer_is_open()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;
        var button = Question(app, help)!;
        var peer = ControlAutomationPeer.CreatePeerForElement(button);

        var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer);
        Assert.Equal(ToggleState.Off, toggle.ToggleState);
        help.IsOpen = true;
        app.Settle();

        Assert.Equal(ToggleState.On, toggle.ToggleState);
    }

    [AvaloniaFact]
    public async Task Enter_and_Space_on_the_focused_question_open_and_close_the_answer()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;
        var button = Question(app, help)!;
        button.Focus(NavigationMethod.Tab);

        app.Press(Key.Space, PhysicalKey.Space);
        Assert.True(help.IsOpen);
        app.Press(Key.Space, PhysicalKey.Space);
        Assert.False(help.IsOpen);
        app.Press(Key.Enter, PhysicalKey.Enter);

        Assert.True(help.IsOpen);
    }

    [AvaloniaFact]
    public async Task Escape_closes_an_open_answer_and_puts_focus_back_on_the_question()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;
        var button = Question(app, help)!;
        button.Focus(NavigationMethod.Tab);
        app.Press(Key.Space, PhysicalKey.Space);
        Assert.True(help.IsOpen);

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(help.IsOpen);
        Assert.False(Answer(app, help)?.IsEffectivelyVisible ?? false);
        Assert.Same(button, app.Focused);
    }

    [AvaloniaFact]
    public async Task Escape_on_the_link_inside_an_answer_closes_the_answer_and_returns_to_the_question()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;
        help.IsOpen = true;
        app.Settle();
        var more = Answer(app, help)!.GetVisualDescendants().OfType<Button>().First(button => button.Name == "MoreButton");
        more.Focus(NavigationMethod.Tab);

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(help.IsOpen);
        Assert.Same(Question(app, help), app.Focused);
    }

    [AvaloniaFact]
    public async Task Opening_an_answer_announces_it_and_closing_it_announces_nothing()
    {
        var (app, help) = await HistoryAsync();
        using var _ = app;

        var opened = Spoken(app, () => help.IsOpen = true);
        var closed = Spoken(app, () => help.IsOpen = false);

        Assert.Equal([help.Answer], opened);
        Assert.Empty(closed);
    }

    [AvaloniaFact]
    public async Task Settings_asks_how_much_space_snapshots_take_under_where_they_are_kept()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var settings = app.Show<SettingsScreenViewModel>(NavDestination.Settings);

        var button = Question(app, settings.SpaceHelp);
        var storage = app.Find<StackPanel>("StorageSection");

        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);
        Assert.Contains(button, storage.GetVisualDescendants());
        button.IsChecked = true;
        app.Settle();
        Assert.True(Answer(app, settings.SpaceHelp)!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_launch_check_shows_both_of_its_explanations_in_place_and_Escape_closes_the_answer_without_closing_the_dialog()
    {
        using var app = await AppStates.OpenAsync("launch-dialog-unsaved");
        var dialog = app.Dialog<LaunchDialogViewModel>();
        var changes = dialog.UnsavedBlocks[0].Help!;

        var savesButton = Question(app, dialog.SaveSnapshotHelp);
        var changesButton = Question(app, changes);

        Assert.NotNull(savesButton);
        Assert.NotNull(changesButton);
        Assert.True(savesButton.IsEffectivelyVisible && changesButton.IsEffectivelyVisible);
        changesButton.Focus(NavigationMethod.Tab);
        app.Press(Key.Space, PhysicalKey.Space);
        Assert.True(changes.IsOpen);
        Assert.True(Answer(app, changes)!.IsEffectivelyVisible);
        Assert.DoesNotContain(Answer(app, changes)!.GetVisualDescendants().OfType<Button>(), button => button.Name == "MoreButton" && button.IsEffectivelyVisible);

        app.Press(Key.Escape, PhysicalKey.Escape);

        Assert.False(changes.IsOpen);
        Assert.True(app.Main.Dialogs.IsOpen);
        Assert.Same(dialog, app.Dialog<LaunchDialogViewModel>());
        Assert.Same(changesButton, app.Focused);
    }

    [AvaloniaFact]
    public async Task The_dialog_after_playing_from_an_older_snapshot_shows_which_to_pick_under_the_note()
    {
        using var app = await AppStates.OpenAsync("post-play-from-older-snapshot");
        var dialog = app.Dialog<PostPlayDialogViewModel>();

        var button = Question(app, dialog.OlderChoiceHelp!);
        var note = app.Find<TextBlock>("OlderNoteText");

        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);
        Assert.True(LayoutAudit.BoundsIn(app.Window, button).Top >= LayoutAudit.BoundsIn(app.Window, note).Bottom - 1, "The question is not under the older-snapshot note.");
    }

    [AvaloniaFact]
    public async Task The_replace_card_shows_its_question_only_while_it_is_the_selected_card()
    {
        using var app = await AppStates.OpenAsync("launch-post-play");
        var dialog = app.Dialog<PostPlayDialogViewModel>();
        var replace = dialog.Choices.Single(choice => choice.Kind == Savepoint.Core.Api.CompletionChoice.UpdateExisting);

        Assert.False(Question(app, replace.Help!)?.IsEffectivelyVisible ?? false);
        dialog.SelectedChoice = replace;
        app.Settle();

        var button = Question(app, replace.Help!);
        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_two_row_states_carry_their_explanation_as_help_text_and_a_tooltip()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        var rows = app.All<Border>().Where(border => border.Classes.Contains("table-row") && border.IsEffectivelyVisible).ToList();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var profile = Assert.IsType<Savepoint.App.Features.Games.ProfileRowViewModel>(row.DataContext);
            Assert.Equal(profile.HelpText, AutomationProperties.GetHelpText(row));
            Assert.Equal(profile.HelpText, ToolTip.GetTip(row));
        }

        Assert.Contains(rows, row => AutomationProperties.GetHelpText(row) == "This profile's files are in your game folders right now.");
        Assert.Contains(rows, row => AutomationProperties.GetHelpText(row) == "Files in your game folders changed since this profile's last snapshot.");
    }
}
