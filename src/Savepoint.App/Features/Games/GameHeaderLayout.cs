using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Games;

/// <summary>Whether the Games header has room for its buttons' words, or shows them as icons so the game's name keeps its room.</summary>
public sealed partial class GameHeaderLayout : ObservableObject
{
    /// <summary>
    /// The sidebar header's width below which its buttons and the layout toggle show icons only. The default 1040 px window
    /// leaves 760, so it keeps its words with about 200 px for the name; anything narrower trades the words for 240 px or more.
    /// </summary>
    public const double SidebarWordsMinWidth = 750;

    /// <summary>The library header's width below which its buttons show icons only.</summary>
    public const double LibraryWordsMinWidth = 560;

    /// <summary>The width below which the Take a snapshot button, which sits on the tab row under the header, shows its icon only.</summary>
    public const double SnapshotWordsMinWidth = 480;

    /// <summary>Whether the header shows its words beside the icons.</summary>
    [ObservableProperty]
    private bool _isCompact;

    /// <summary>Whether the Take a snapshot button on the tab row shows only its icon.</summary>
    [ObservableProperty]
    private bool _isSnapshotCompact;

    /// <summary>Chooses the header that fits a screen of this width.</summary>
    /// <param name="width">The header's width in device-independent pixels.</param>
    /// <param name="sidebarLayout">True for the sidebar layout, whose header also holds the game's art and the layout toggle.</param>
    public void Update(double width, bool sidebarLayout)
    {
        IsCompact = width < (sidebarLayout ? SidebarWordsMinWidth : LibraryWordsMinWidth);
        IsSnapshotCompact = width < SnapshotWordsMinWidth;
    }
}
