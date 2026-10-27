using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Library;

/// <summary>One game the confirmation names.</summary>
/// <param name="Name">The game's name.</param>
/// <param name="InstallPath">Its install folder, fully resolved.</param>
/// <param name="SavesLine">Its saves folder, or the plain statement that it has none yet.</param>
/// <param name="InstallLine">Its install folder with its label ("Game folder: ...").</param>
/// <param name="ModsLine">Its mods folder with its label ("Mods folder: ..."); empty when the game has none to add.</param>
public sealed record LibraryConfirmEntry(string Name, string InstallPath, string SavesLine, string InstallLine = "", string ModsLine = "");

/// <summary>
/// The one confirmation before several library games are added: it names every game with each folder SavePoint will manage, and says
/// what managing a folder means. It answers true only when the player confirms; Escape and Cancel answer false.
/// </summary>
public sealed partial class LibraryConfirmDialogViewModel : DialogViewModel<bool>
{
    /// <param name="games">The games to add, in the order shown; at least one.</param>
    /// <param name="text">Formats the copy.</param>
    public LibraryConfirmDialogViewModel(IReadOnlyList<LibraryGame> games, UiFormatter text)
        : base(text.Message("library.confirm.title", ("count", games.Count)))
    {
        // A saves or mods folder that is not there yet is left out when the game is added, so it is not named as one SavePoint will manage.
        Entries =
        [
            .. games.Select(game => new LibraryConfirmEntry(
                game.Name,
                game.InstallPath,
                game.SavesFolder is { } saves && game.SavesFolderExists
                    ? text.Message("library.confirm.savesLine", ("path", saves))
                    : text.Message("library.confirm.noSaves"),
                text.Message("library.confirm.installLine", ("path", game.InstallPath)),
                game.ModsFolder is { } mods && game.ModsFolderExists ? text.Message("library.confirm.modsLine", ("path", mods)) : string.Empty)),
        ];
        Message = text.Message("library.confirm.message");
        ConfirmLabel = text.Message("library.confirm.confirm");
        CancelLabel = text.Message("common.cancel");
    }

    /// <summary>Each game with its folders.</summary>
    public IReadOnlyList<LibraryConfirmEntry> Entries { get; }

    /// <summary>What confirming means, in plain words.</summary>
    public string Message { get; }

    /// <summary>The confirm button's text.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The cancel button's text.</summary>
    public string CancelLabel { get; }

    /// <inheritdoc />
    public override string Description => Message;

    /// <inheritdoc />
    public override double DialogWidth => 640;

    /// <inheritdoc />
    public override bool PinsFooter => true;

    /// <inheritdoc />
    protected override bool DismissResult => false;

    [RelayCommand]
    private void Confirm() => Close(true);

    [RelayCommand]
    private void Cancel() => Close(false);
}
