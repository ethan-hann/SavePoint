using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform;

/// <summary>Reads a <see cref="PlatformEnvironment"/> through one OS's path rules; anything not absolute counts as unset.</summary>
internal sealed class PlatformPaths
{
    private readonly PlatformEnvironment _environment;

    public PlatformPaths(PathSyntax syntax, PlatformEnvironment environment)
    {
        if (!syntax.IsAbsolute(environment.HomeDirectory))
        {
            throw new ArgumentException(
                $"The home directory '{environment.HomeDirectory}' is not an absolute {syntax.Os} path.",
                nameof(environment));
        }

        Syntax = syntax;
        _environment = environment;
        Home = syntax.Normalize(environment.HomeDirectory);
    }

    public PathSyntax Syntax { get; }

    public string Home { get; }

    public string? Variable(string name) => AbsoluteOrNull(_environment.GetVariable(name));

    public string? RawVariable(string name)
    {
        var value = _environment.GetVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public string? Known(KnownFolder folder) => AbsoluteOrNull(_environment.GetKnownFolder(folder));

    public string? SteamFolder() => AbsoluteOrNull(_environment.GetSteamFolder());

    public string? ReadFile(string path) => _environment.ReadTextFile(path);

    public IReadOnlyList<string> LocalHostNames() => _environment.LocalHostNames();

    public IEnumerable<string> DriveRoots() =>
        _environment.ListDriveRoots().Select(AbsoluteOrNull).OfType<string>().Where(IsRoot);

    public string Under(string basePath, string relative) => Syntax.Combine(basePath, relative);

    public bool IsRoot(string absolutePath) => Syntax.Split(absolutePath, collapseDots: true).Parts.Count == 0;

    /// <summary>The last component, or null at a root.</summary>
    public string? Name(string absolutePath) => Syntax.Split(absolutePath, collapseDots: true).Parts.LastOrDefault();

    /// <summary>The containing folder, or null at a root.</summary>
    public string? Parent(string absolutePath)
    {
        var parsed = Syntax.Split(absolutePath, collapseDots: true);
        return parsed.Parts.Count == 0 ? null : Syntax.Join(parsed.Root, [.. parsed.Parts.Take(parsed.Parts.Count - 1)]);
    }

    private string? AbsoluteOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Syntax.IsAbsolute(path) ? null : Syntax.Normalize(path);
}
