using Avalonia.Input;

namespace Savepoint.App.Features.Help;

/// <summary>The one key an open explanation answers to.</summary>
internal static class ContextualHelpKeys
{
    /// <summary>
    /// Escape closes an open answer. The key press is marked handled, so a dialog the explanation sits in stays open: the first
    /// Escape closes the answer, and only a second one, with nothing open, reaches the dialog.
    /// </summary>
    /// <param name="e">The key press, which came from inside a question or an answer.</param>
    /// <param name="dataContext">The explanation the question or answer belongs to.</param>
    public static void CloseOnEscape(KeyEventArgs e, object? dataContext)
    {
        if (e.Key == Key.Escape && !e.Handled && dataContext is ContextualHelpViewModel { IsOpen: true } help)
        {
            help.Close();
            e.Handled = true;
        }
    }
}
