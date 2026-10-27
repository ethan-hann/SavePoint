namespace Savepoint.Core.Catalog;

/// <summary>
/// A game SavePoint manages: where its files are, how to start it, and what to leave out. Stored in <c>games.json</c>, and also
/// the shape of an exported definition file.
/// </summary>
/// <param name="SchemaVersion">The catalog schema this definition was written for; only <see cref="CurrentSchemaVersion"/> is accepted.</param>
/// <param name="Id">
/// Stable id, letters, digits, <c>_</c>, and <c>-</c>, starting with a letter or digit. It names the game's folder under the storage
/// root, so it never holds a separator or dot segment. <see cref="CatalogIds.NewGameId"/> makes one.
/// </param>
/// <param name="Name">The name the user sees. Required.</param>
/// <param name="Install">The install folder (program files and mods).</param>
/// <param name="External">The optional second folder, outside the install, that holds saves; null when the game has none.</param>
/// <param name="Launch">How to start the game.</param>
/// <param name="IgnorePaths">
/// Patterns for files SavePoint never saves, kept in SavePoint's own exclude list and never written into the game folder (D13.1).
/// Empty for none.
/// </param>
/// <param name="ModPathHints">
/// Folders or files, relative to the install folder, used to count a profile's mod files. Empty when the game names none.
/// </param>
/// <param name="Notes">Free text the user keeps about the game, or null. It survives editing (D13.22).</param>
/// <param name="SteamAppId">
/// The game's Steam app id, a positive number, or null when the user named none. A stored id always wins over one found by looking
/// at the game's launch link or folders (D30). It picks the artwork and the Steam Cloud saves to look for. It does not say whether the
/// game needs the Steam client running: copies from GOG or a disc carry the app id of the Steam game they matched, so a launch asks for
/// Steam only when the install folder is in a Steam library (D67).
/// </param>
/// <param name="InstallIgnorePaths">
/// Patterns for files SavePoint never saves from the install folder only. <paramref name="IgnorePaths"/> applies to every folder; these
/// never reach the saves folder, so a log rule here cannot hide a save. Empty for none, and absent from older files, which read as empty.
/// </param>
/// <param name="Mods">
/// The optional third folder, outside the install, that holds the game's mods; null when the game has none. Written as an extra
/// <c>mods</c> key beside <c>external</c>, which an older build ignores, so a catalog that holds one still reads there.
/// </param>
public sealed record GameDefinition(
    int SchemaVersion = GameDefinition.CurrentSchemaVersion,
    string Id = "",
    string Name = "",
    InstallLocation? Install = null,
    ExternalLocation? External = null,
    LaunchConfig? Launch = null,
    IReadOnlyList<string>? IgnorePaths = null,
    IReadOnlyList<string>? ModPathHints = null,
    string? Notes = null,
    int? SteamAppId = null,
    IReadOnlyList<string>? InstallIgnorePaths = null,
    ModsLocation? Mods = null)
{
    /// <summary>The only schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    // The members below are redeclared so an absent or null JSON value reads as empty instead of null.

    /// <summary>The install folder; never null, an empty location when the JSON had none.</summary>
    public InstallLocation Install { get; init; } = Install ?? new();

    /// <summary>How to start the game; never null, the default program launch when the JSON had none.</summary>
    public LaunchConfig Launch { get; init; } = Launch ?? new();

    /// <summary>The ignore patterns; empty, never null, when the JSON had none.</summary>
    public IReadOnlyList<string> IgnorePaths { get; init; } = IgnorePaths ?? [];

    /// <summary>The mod path hints; empty, never null, when the JSON had none.</summary>
    public IReadOnlyList<string> ModPathHints { get; init; } = ModPathHints ?? [];

    /// <summary>The install-only ignore patterns; empty, never null, when the JSON had none.</summary>
    public IReadOnlyList<string> InstallIgnorePaths { get; init; } = InstallIgnorePaths ?? [];

    /// <summary>
    /// The patterns the folder with this role leaves out: the game-wide <see cref="IgnorePaths"/>, and for the install folder also
    /// <see cref="InstallIgnorePaths"/>. The saves folder never gets the install-only ones. The install-only rules come first, so a
    /// later negation in <see cref="IgnorePaths"/> (such as <c>!keep.log</c>) can override one of them; the last matching rule wins.
    /// </summary>
    /// <param name="role">The folder.</param>
    public IReadOnlyList<string> IgnorePathsFor(RepoRole role) =>
        role == RepoRole.Install && InstallIgnorePaths.Count > 0 ? RunTimeOutput.Merge(InstallIgnorePaths, IgnorePaths) : IgnorePaths;

    /// <summary>Compares the lists by content, not by reference, so the editor can tell whether anything changed.</summary>
    public bool Equals(GameDefinition? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Id == other.Id
        && Name == other.Name
        && Install == other.Install
        && External == other.External
        && Mods == other.Mods
        && Launch == other.Launch
        && Notes == other.Notes
        && SteamAppId == other.SteamAppId
        && IgnorePaths.SequenceEqual(other.IgnorePaths)
        && ModPathHints.SequenceEqual(other.ModPathHints)
        && InstallIgnorePaths.SequenceEqual(other.InstallIgnorePaths);

    /// <summary>A hash that agrees with <see cref="Equals(GameDefinition)"/>: it covers the lists' content.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Install);
        hash.Add(External);
        hash.Add(Mods);
        hash.Add(Launch);
        hash.Add(Notes);
        hash.Add(SteamAppId);
        foreach (var path in IgnorePaths)
        {
            hash.Add(path);
        }

        foreach (var hint in ModPathHints)
        {
            hash.Add(hint);
        }

        foreach (var path in InstallIgnorePaths)
        {
            hash.Add(path);
        }

        return hash.ToHashCode();
    }
}
