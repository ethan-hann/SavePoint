namespace Savepoint.Core.Catalog;

/// <summary>How to start a game and how to tell it has stopped.</summary>
/// <param name="Method">Whether to start a program or open a link. Decides which of the next two fields is used.</param>
/// <param name="ExePath">
/// For <see cref="LaunchMethod.Exe"/>: the program, relative to the install folder, with no <c>..</c> and not rooted. Required
/// then; ignored for a link.
/// </param>
/// <param name="Uri">
/// For <see cref="LaunchMethod.Uri"/>: the link to open, which must start with a scheme such as <c>steam:</c>. Required then.
/// </param>
/// <param name="Args">Command-line arguments passed to the program as they are, in order, with no token expansion. Empty for none.</param>
/// <param name="ProcessNameForExitDetection">
/// The exact process name to watch when the program SavePoint starts is only a launcher that hands over to the real game, or when
/// the game starts from a link. Null to follow the started program itself. Matching ignores case on Windows and macOS and a
/// trailing <c>.exe</c>; on Linux it also matches the executable's file name (D13.12).
/// </param>
public sealed record LaunchConfig(
    LaunchMethod Method = LaunchMethod.Exe,
    string? ExePath = null,
    string? Uri = null,
    IReadOnlyList<string>? Args = null,
    string? ProcessNameForExitDetection = null)
{
    /// <summary>The program's arguments; empty, never null, when the JSON had none.</summary>
    // Redeclared so an absent or null JSON array reads as empty instead of null.
    public IReadOnlyList<string> Args { get; init; } = Args ?? [];

    /// <summary>Compares the arguments by content, not by reference, so the editor can tell whether anything changed.</summary>
    public bool Equals(LaunchConfig? other) =>
        other is not null
        && Method == other.Method
        && ExePath == other.ExePath
        && Uri == other.Uri
        && ProcessNameForExitDetection == other.ProcessNameForExitDetection
        && Args.SequenceEqual(other.Args);

    /// <summary>A hash that agrees with <see cref="Equals(LaunchConfig)"/>: it covers the arguments' content.</summary>
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
