using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Api;

namespace Savepoint.App.Features.Library;

/// <summary>One installed game in the "Add games from your libraries" list: what it is, where it is, and whether it is checked.</summary>
public sealed class LibraryRowViewModel : ObservableObject
{
    private bool _isSelected;

    /// <param name="game">The game as the scan reported it.</param>
    /// <param name="text">Formats the lines.</param>
    public LibraryRowViewModel(LibraryGame game, UiFormatter text)
    {
        Game = game;
        Name = game.Name;
        StoreLabel = text.Message(game.Source == LibrarySource.Steam ? "library.row.store.steam" : "library.row.store.gog");
        InstallLine = text.Message("library.row.install", ("path", game.InstallPath));
        SavesLine = SavesLineFor(game, text);
        StatusText = game.AlreadyAdded ? text.Message("library.row.added") : string.Empty;
        AutomationName = StatusText.Length == 0
            ? text.Message("library.row.automation", ("name", Name), ("store", StoreLabel), ("path", game.InstallPath))
            : text.Message("library.row.automationAdded", ("name", Name), ("store", StoreLabel), ("status", StatusText), ("path", game.InstallPath));
    }

    /// <summary>The game as the scan reported it.</summary>
    public LibraryGame Game { get; }

    /// <summary>The game's name.</summary>
    public string Name { get; }

    /// <summary>The store the game was found in ("Steam" or "GOG Galaxy").</summary>
    public string StoreLabel { get; }

    /// <summary>Where the game is installed.</summary>
    public string InstallLine { get; }

    /// <summary>What SavePoint knows about the game's saves folder, in plain words.</summary>
    public string SavesLine { get; }

    /// <summary>"Already added" for a game in the library; empty otherwise.</summary>
    public string StatusText { get; }

    /// <summary>The name a screen reader announces: the game, its store, and "Already added" when that applies.</summary>
    public string AutomationName { get; }

    /// <summary>True when the game is already in the library.</summary>
    public bool IsAlreadyAdded => Game.AlreadyAdded;

    /// <summary>True when the player may check this game.</summary>
    public bool CanSelect => !Game.AlreadyAdded;

    /// <summary>True while the game is checked. A game that is already added cannot be checked.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value && CanSelect);
    }

    private static string SavesLineFor(LibraryGame game, UiFormatter text) => game.SavesFolder switch
    {
        null => text.Message("library.row.noSaves"),
        { } path when game.SavesFolderExists => text.Message("library.row.saves", ("path", path)),
        { } path => text.Message("library.row.savesNotYet", ("path", path)),
    };
}
