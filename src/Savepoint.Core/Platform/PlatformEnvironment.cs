namespace Savepoint.Core.Platform;

/// <summary>
/// Everything a platform reads from the machine, so each OS's tables can be built from a fake on any host.
/// </summary>
public sealed record PlatformEnvironment
{
    public required string HomeDirectory { get; init; }

    public Func<string, string?> GetVariable { get; init; } = static _ => null;

    public Func<KnownFolder, string?> GetKnownFolder { get; init; } = static _ => null;

    public Func<IReadOnlyList<string>> ListDriveRoots { get; init; } = static () => [];

    /// <summary>Reads a small text file (the Linux user-dirs.dirs); null when it is missing or unreadable.</summary>
    public Func<string, string?> ReadTextFile { get; init; } = static _ => null;

    public static PlatformEnvironment FromCurrentMachine() => new()
    {
        HomeDirectory = "",
    };
}
