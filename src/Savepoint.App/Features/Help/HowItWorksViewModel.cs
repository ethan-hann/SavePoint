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
    /// <param name="showTour">Closes the page and shows the first-run tour; null leaves the page without its closing button.</param>
    /// <param name="canShowTour">Says whether the tour can run now; it needs a game with a profile to point at. A tour with nothing to show leaves the button off.</param>
    /// <param name="section">The topic the page opens at; null opens it at the top.</param>
    public HowItWorksViewModel(UiFormatter text, Action close, Action? showTour = null, Func<bool>? canShowTour = null, HowItWorksSection? section = null)
    {
        Section = section;
        TourLabel = text.Message("help.page.tour");
        HasTour = showTour is not null;
        CanShowTour = canShowTour?.Invoke() ?? HasTour;
        ShowTourCommand = new RelayCommand(() => showTour?.Invoke(), () => CanShowTour);
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
            new(
                text.Message("help.page.starts.title"),
                text.Message("help.page.starts.body"),
                HelpPicture.None,
                [],
                [text.Message("help.page.starts.stopped")],
                HowItWorksSection.HowTheGameStarts),
            new(
                text.Message("help.page.files.title"),
                text.Message("help.page.files.body"),
                HelpPicture.None,
                [text.Message("help.page.files.both"), text.Message("help.page.files.install")],
                [text.Message("help.page.files.savesRule")],
                HowItWorksSection.FilesToLeaveOut),
            new(
                text.Message("help.page.cloud.title"),
                text.Message("help.page.cloud.body"),
                HelpPicture.None,
                [],
                [text.Message("help.page.cloud.turnOff")],
                HowItWorksSection.CloudSaves),
            PermanentTopic(text),
        ];
        BackCommand = new RelayCommand(close);
    }

    /// <summary>Raised when the page should move to <see cref="Section"/> while it is already open.</summary>
    public event EventHandler? SectionRequested;

    /// <summary>The topic the page opens at, or null for the top.</summary>
    public HowItWorksSection? Section { get; private set; }

    /// <summary>Moves the open page to a topic.</summary>
    /// <param name="section">The topic to show.</param>
    public void ShowSection(HowItWorksSection section)
    {
        Section = section;
        SectionRequested?.Invoke(this, EventArgs.Empty);
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

    /// <summary>The text of the button at the end of the page that shows the tour again.</summary>
    public string TourLabel { get; }

    /// <summary>True when the page ends with a button for the tour.</summary>
    public bool HasTour { get; }

    /// <summary>True when the tour can run now, which is when its button works.</summary>
    public bool CanShowTour { get; }

    /// <summary>Closes the page and shows the first-run tour from its first step.</summary>
    public IRelayCommand ShowTourCommand { get; }

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
