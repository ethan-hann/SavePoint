using System.Globalization;
using Savepoint.Core.Artwork;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>
/// What Steam's manifest for a game says about its install: the build, when it last changed, and whether an update is running.
/// Read only; SavePoint never writes a manifest.
/// </summary>
/// <param name="BuildId">The installed build, or null when the manifest has none.</param>
/// <param name="LastUpdated">When Steam last updated the game, as the manifest writes it, or null.</param>
/// <param name="StateFlags">Steam's state flags for the game; 0 when the manifest has none.</param>
internal sealed record SteamInstallState(string? BuildId, string? LastUpdated, long StateFlags)
{
    // Steam's flags for work in progress on the install: an update running or started, a download, files being staged, committed or
    // added, space being reserved, or a reconfigure. 4 (fully installed) and 2 (an update waiting, not started) are not among them.
    private const long _workInProgress = 256 | 1024 | 65536 | 262144 | 524288 | 1048576 | 2097152 | 4194304;

    // A manifest is a few KB; anything bigger is something else.
    private const long _manifestLimit = 1 << 20;

    /// <summary>True when Steam is working on the install right now, so its files are changing or about to.</summary>
    public bool IsUpdating => (StateFlags & _workInProgress) != 0;

    /// <summary>True when the build or the update time differs from an earlier reading: Steam finished an update since then.</summary>
    /// <param name="earlier">The reading to compare with, or null when there was no manifest then.</param>
    public bool ChangedSince(SteamInstallState? earlier) =>
        earlier is not null && (!string.Equals(BuildId, earlier.BuildId, StringComparison.Ordinal) || !string.Equals(LastUpdated, earlier.LastUpdated, StringComparison.Ordinal));

    /// <summary>
    /// Reads the manifest of the game installed at this folder. Null when the folder is not in a Steam library, no manifest names it, or the
    /// manifest cannot be read.
    /// </summary>
    /// <param name="installPath">The install folder, or null when it cannot be resolved.</param>
    /// <param name="pathComparison">How the OS compares paths.</param>
    public static SteamInstallState? Read(string? installPath, StringComparison pathComparison)
    {
        if (SteamAppIdDetector.ManifestOf(installPath, pathComparison) is not { } manifest)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(manifest);
            if (info.Length > _manifestLimit)
            {
                return null;
            }

            var state = ValveKeyValues.ParseRoot(File.ReadAllText(manifest), "AppState");
            if (state is null)
            {
                return null;
            }

            _ = long.TryParse(state.TextOf("StateFlags"), NumberStyles.None, CultureInfo.InvariantCulture, out var flags);
            return new SteamInstallState(state.TextOf("buildid"), state.TextOf("LastUpdated"), flags);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
