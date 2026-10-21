using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Games;

/// <summary>
/// Which optional columns of the profile table fit. The header and every row bind to the one instance, so a narrow
/// window drops the mod-files column first and the version column next, everywhere at once.
/// </summary>
public sealed partial class ProfileTableColumns : ObservableObject
{
    /// <summary>The table's width below which the mod-files column is dropped.</summary>
    public const double ModFilesMinWidth = 680;

    /// <summary>The table's width below which the version column is dropped too.</summary>
    public const double VersionMinWidth = 520;

    /// <summary>Whether the version column is shown.</summary>
    [ObservableProperty]
    private bool _showVersion = true;

    /// <summary>Whether the mod-files column is shown.</summary>
    [ObservableProperty]
    private bool _showModFiles = true;

    /// <summary>Chooses the columns that fit a table of this width.</summary>
    /// <param name="width">The table's width in device-independent pixels.</param>
    public void Update(double width)
    {
        ShowModFiles = width >= ModFilesMinWidth;
        ShowVersion = width >= VersionMinWidth;
    }
}
