using Savepoint.App.Features.Launch;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Fakes;

internal static class PostPlayDialogExtensions
{
    // Picks the Not now card and presses the main button, the way a player leaves the saves for later.
    public static void ChooseNotNow(this PostPlayDialogViewModel dialog)
    {
        dialog.SelectedChoice = dialog.Choices.Single(choice => choice.Kind == CompletionChoice.Skip);
        dialog.SaveCommand.Execute(null);
    }
}
