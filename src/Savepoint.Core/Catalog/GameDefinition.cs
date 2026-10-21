namespace Savepoint.Core.Catalog;

public sealed record GameDefinition(
    int SchemaVersion = GameDefinition.CurrentSchemaVersion,
    string Id = "",
    string Name = "",
    InstallLocation? Install = null,
    ExternalLocation? External = null,
    LaunchConfig? Launch = null,
    IReadOnlyList<string>? IgnorePaths = null,
    IReadOnlyList<string>? ModPathHints = null,
    string? Notes = null)
{
    public const int CurrentSchemaVersion = 1;

    // The members below are redeclared so an absent or null JSON value reads as empty instead of null.
    public InstallLocation Install { get; init; } = Install ?? new();

    public LaunchConfig Launch { get; init; } = Launch ?? new();

    public IReadOnlyList<string> IgnorePaths { get; init; } = IgnorePaths ?? [];

    public IReadOnlyList<string> ModPathHints { get; init; } = ModPathHints ?? [];

    // Record equality would compare the lists by reference, which breaks "unchanged" checks in the editor.
    public bool Equals(GameDefinition? other) =>
        other is not null
        && SchemaVersion == other.SchemaVersion
        && Id == other.Id
        && Name == other.Name
        && Install == other.Install
        && External == other.External
        && Launch == other.Launch
        && Notes == other.Notes
        && IgnorePaths.SequenceEqual(other.IgnorePaths)
        && ModPathHints.SequenceEqual(other.ModPathHints);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Install);
        hash.Add(External);
        hash.Add(Launch);
        hash.Add(Notes);
        foreach (var path in IgnorePaths)
        {
            hash.Add(path);
        }

        foreach (var hint in ModPathHints)
        {
            hash.Add(hint);
        }

        return hash.ToHashCode();
    }
}
