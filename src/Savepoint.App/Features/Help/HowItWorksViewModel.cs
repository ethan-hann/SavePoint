using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Formatting;

namespace Savepoint.App.Features.Help;

/// <summary>
/// The "How SavePoint works" page: the few ideas the app uses, in the order a player meets them, each in a short topic with a
/// picture. It is a full pane that replaces the screen it was opened from until Back or Escape closes it.
/// </summary>
public sealed class HowItWorksViewModel : ObservableObject
{
    /// <param name="text">Formats the page's copy.</param>
    /// <param name="close">Closes the page and shows the screen it was opened from.</param>
    public HowItWorksViewModel(UiFormatter text, Action close)
    {
        Title = text.Message("help.page.title");
        BackName = text.Message("help.page.back");
        Intro = text.Message("help.page.intro");
        Topics =
        [
            new(text.Message("help.page.basics.title"), text.Message("help.page.basics.body"), HelpPicture.Basics, [], []),
            new(text.Message("help.page.saves.title"), text.Message("help.page.saves.body"), HelpPicture.Saves, [], []),
            new(text.Message("help.page.play.title"), text.Message("help.page.play.body"), HelpPicture.Play, [], []),
            new(text.Message("help.page.newChanges.title"), text.Message("help.page.newChanges.body"), HelpPicture.NewChanges, [], []),
            new(text.Message("help.page.afterPlay.title"), text.Message("help.page.afterPlay.body"), HelpPicture.AfterPlay, [], []),
            new(text.Message("help.page.older.title"), text.Message("help.page.older.body"), HelpPicture.Older, [], []),
            new(text.Message("help.page.removed.title"), text.Message("help.page.removed.body"), HelpPicture.Removed, [], []),
            new(text.Message("help.page.activity.title"), text.Message("help.page.activity.body"), HelpPicture.Activity, [], []),
            new(text.Message("help.page.space.title"), text.Message("help.page.space.body"), HelpPicture.Space, [], []),
            PermanentTopic(text),
        ];
        BackCommand = new RelayCommand(close);
    }

    /// <summary>The page's heading.</summary>
    public string Title { get; }

    /// <summary>The back arrow's accessible name.</summary>
    public string BackName { get; }

    /// <summary>One sentence under the heading.</summary>
    public string Intro { get; }

    /// <summary>The topics, in reading order.</summary>
    public IReadOnlyList<HowItWorksTopic> Topics { get; }

    /// <summary>Closes the page.</summary>
    public IRelayCommand BackCommand { get; }

    // The last topic is a plain list with no picture: what a player cannot take back, and what they can.
    private static HowItWorksTopic PermanentTopic(UiFormatter text) =>
        new(
            text.Message("help.page.permanent.title"),
            text.Message("help.page.permanent.body"),
            HelpPicture.None,
            [
                text.Message("help.page.permanent.discard"),
                text.Message("help.page.permanent.freeUp"),
                text.Message("help.page.permanent.delete"),
                text.Message("help.page.permanent.removeAll"),
                text.Message("help.page.permanent.emptyFolder"),
            ],
            [
                text.Message("help.page.permanent.noteRecoverable"),
                text.Message("help.page.permanent.noteClosing"),
            ]);
}
