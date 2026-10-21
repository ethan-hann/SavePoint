using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Shell;

/// <summary>
/// One game in the rail or on the shelf: its definition, how many profiles it has and how much space SavePoint keeps
/// for it. Counts and sizes are null until the shell has loaded them, and the matching text is then empty.
/// </summary>
public sealed partial class GameRowViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool _showLabels = true;

    /// <param name="game">The game's definition.</param>
    /// <param name="profileCount">Its number of profiles; null when not known.</param>
    /// <param name="bytes">The space SavePoint keeps for it; null when not known.</param>
    /// <param name="text">Formats the count and size.</param>
    /// <param name="art">The game's artwork for its tiles; null leaves the tile its name tint.</param>
    public GameRowViewModel(GameDefinition game, int? profileCount, long? bytes, UiFormatter text, GameArt? art = null)
    {
        Game = game;
        Art = art;
        ProfileCount = profileCount;
        Bytes = bytes;
        ProfileCountText = profileCount is { } count ? text.Message("shell.profileCount", ("count", count)) : string.Empty;
        var size = bytes is { } used ? text.FileSize(used) : null;
        LibrarySubtitle = profileCount is null ? string.Empty
            : size is null ? ProfileCountText
            : text.Message("shell.librarySubtitle", ("profiles", ProfileCountText), ("size", size));
        AutomationName = profileCount is null ? game.Name : text.Message("shell.gameAutomationName", ("name", game.Name), ("profiles", ProfileCountText));
    }

    /// <summary>The game's definition.</summary>
    public GameDefinition Game { get; }

    /// <summary>The game's artwork for its tiles, shared by every tile of the game; null when the row was made without any.</summary>
    public GameArt? Art { get; }

    /// <summary>The game's id.</summary>
    public string Id => Game.Id;

    /// <summary>The game's name.</summary>
    public string Name => Game.Name;

    /// <summary>The number of profiles; null when not known.</summary>
    public int? ProfileCount { get; }

    /// <summary>The space SavePoint keeps for the game; null when not known.</summary>
    public long? Bytes { get; }

    /// <summary>"3 profiles", or empty when the count is not known.</summary>
    public string ProfileCountText { get; }

    /// <summary>"3 profiles · 41.2 GB" for the shelf card; the count alone when the size is not known.</summary>
    public string LibrarySubtitle { get; }

    /// <summary>The name and count read as one phrase for assistive technology.</summary>
    public string AutomationName { get; }

    /// <summary>The tooltip shown when the rail is collapsed to icons and the name is hidden; null while the name is visible.</summary>
    public string? ToolTipText => ShowLabels ? null : AutomationName;
}
