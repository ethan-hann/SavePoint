namespace Savepoint.App.Features.Help;

/// <summary>What the tour's card says at one step.</summary>
/// <param name="Step">What the step points at.</param>
/// <param name="Title">The card's heading.</param>
/// <param name="Body">One or two sentences.</param>
/// <param name="Position">"Step 2 of 4".</param>
/// <param name="AutomationName">The card's accessible name: the position and the title.</param>
/// <param name="CanGoBack">False on the first step.</param>
/// <param name="NextLabel">"Next", or "Done" on the last step.</param>
/// <param name="FindAgain">On the last step, where the tour can be shown again; empty on the others.</param>
public sealed record TourStepViewModel(TourStep Step, string Title, string Body, string Position, string AutomationName, bool CanGoBack, string NextLabel, string FindAgain)
{
    /// <summary>True when the card says where to find the tour again.</summary>
    public bool HasFindAgain => FindAgain.Length > 0;
}
