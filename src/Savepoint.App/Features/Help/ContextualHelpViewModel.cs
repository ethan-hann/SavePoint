using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Savepoint.App.Features.Help;

/// <summary>
/// One "What's this?" explanation: a question the player would ask, written as a text button beside the idea it explains, and an
/// answer that opens in place below it. The answer starts collapsed so a player who knows the idea sees one short link. It is the
/// same object for the button and for the answer, so the two views stay in step.
/// </summary>
public sealed partial class ContextualHelpViewModel : ObservableObject
{
    /// <summary>True while the answer shows.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <param name="question">The button's text, worded as the player would ask it.</param>
    /// <param name="answer">The text that opens below the button.</param>
    /// <param name="moreLabel">The text of the link to the "How SavePoint works" page; empty when the answer has no link.</param>
    /// <param name="openHowItWorks">Opens the page; null for an answer that sits in a dialog, where a page opened behind it could not be used.</param>
    /// <param name="picture">A picture drawn small inside the answer; <see cref="HelpPicture.None"/> for none.</param>
    /// <param name="automationName">The button's accessible name when it must differ from <paramref name="question"/>; empty uses the question.</param>
    public ContextualHelpViewModel(string question, string answer, string moreLabel = "", Action? openHowItWorks = null, HelpPicture picture = HelpPicture.None, string automationName = "")
    {
        AutomationName = automationName.Length > 0 ? automationName : question;
        Picture = picture;
        Question = question;
        Answer = answer;
        MoreLabel = moreLabel;
        HasLink = openHowItWorks is not null && moreLabel.Length > 0;
        OpenHowItWorksCommand = new RelayCommand(() =>
        {
            IsOpen = false;
            openHowItWorks?.Invoke();
        });
    }

    /// <summary>Raised when the question button should take focus, because the answer that had it closed.</summary>
    public event EventHandler? FocusQuestionRequested;

    /// <summary>The button's text, such as "What's a snapshot?"; it is also the button's accessible name.</summary>
    public string Question { get; }

    /// <summary>The button's accessible name; it is <see cref="Question"/> unless the same question is asked in several places and each needs its own name.</summary>
    public string AutomationName { get; }

    /// <summary>The text that opens below the button.</summary>
    public string Answer { get; }

    /// <summary>A picture drawn small inside the answer, from the "How SavePoint works" page; <see cref="HelpPicture.None"/> for none.</summary>
    public HelpPicture Picture { get; }

    /// <summary>True when the answer draws a picture.</summary>
    public bool HasPicture => Picture != HelpPicture.None;

    /// <summary>The text of the link to the page; empty when there is none.</summary>
    public string MoreLabel { get; }

    /// <summary>True when the answer ends with a link to the "How SavePoint works" page.</summary>
    public bool HasLink { get; }

    /// <summary>Closes the answer and opens the "How SavePoint works" page.</summary>
    public IRelayCommand OpenHowItWorksCommand { get; }

    /// <summary>Closes an open answer and gives focus back to the question button, which is what Escape does; does nothing when it is closed.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        FocusQuestionRequested?.Invoke(this, EventArgs.Empty);
    }
}
