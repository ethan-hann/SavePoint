namespace Savepoint.Core.Catalog;

// ExePath is relative to the install folder; Args are passed to the executable verbatim.
public sealed record LaunchConfig(
    LaunchMethod Method = LaunchMethod.Exe,
    string? ExePath = null,
    string? Uri = null,
    IReadOnlyList<string>? Args = null,
    string? ProcessNameForExitDetection = null)
{
    // Redeclared so an absent or null JSON array reads as empty instead of null.
    public IReadOnlyList<string> Args { get; init; } = Args ?? [];

    // Record equality would compare Args by reference, which breaks "unchanged" checks in the editor.
    public bool Equals(LaunchConfig? other) =>
        other is not null
        && Method == other.Method
        && ExePath == other.ExePath
        && Uri == other.Uri
        && ProcessNameForExitDetection == other.ProcessNameForExitDetection
        && Args.SequenceEqual(other.Args);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        hash.Add(ExePath);
        hash.Add(Uri);
        hash.Add(ProcessNameForExitDetection);
        foreach (var arg in Args)
        {
            hash.Add(arg);
        }

        return hash.ToHashCode();
    }
}
