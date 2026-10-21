using System.Text.RegularExpressions;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>Resolves a path to the folder it really names by following every symlink and junction on the way.</summary>
public static partial class RealPath
{
    // Matches the Linux symlink limit; a longer chain is a cycle for any practical purpose.
    private const int _maxLinkHops = 40;

    /// <summary>Resolves against the running machine; a relative path is taken from the current directory.</summary>
    public static string Resolve(string path)
    {
        var syntax = PathSyntax.For(PlatformFactory.CurrentOs);
        var absolute = syntax.IsAbsolute(path) ? path : Path.GetFullPath(path);
        return Resolve(absolute, syntax, PathProbes.Host);
    }

    /// <summary>
    /// A dangling link is replaced by its target's text so the check sees where a write would land; missing components are kept.
    /// A Windows link to a whole volume is left in place (the mount point is where the user sees the data).
    /// </summary>
    /// <param name="path">An absolute path in <paramref name="syntax"/>'s style.</param>
    /// <param name="syntax">The OS whose path rules apply.</param>
    /// <param name="probe">Reads the links.</param>
    /// <exception cref="ArgumentException">The path is not absolute.</exception>
    /// <exception cref="IOException">There are more than 40 links in a chain, or a Windows link leads to a volume or device name that cannot be traced to a folder.</exception>
    public static string Resolve(string path, PathSyntax syntax, IPathProbe probe)
    {
        var isWindows = syntax.Os == OsKind.Windows;

        // Win32 collapses ".." before any file system sees the path; POSIX applies it to the real directory.
        var start = syntax.Split(path, collapseDots: isWindows);
        var root = start.Root;
        var resolved = new List<string>();
        var pending = new Stack<string>();
        PushParts(pending, start.Parts);
        var hops = 0;

        while (pending.Count > 0)
        {
            var part = pending.Pop();
            if (part == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }

                continue;
            }

            var candidate = syntax.Join(root, [.. resolved, part]);
            var target = probe.ReadLinkTarget(candidate);
            if (target is not null)
            {
                var stripped = isWindows ? StripNamespacePrefix(target) : target;
                if (isWindows && IsNamespaceForm(stripped))
                {
                    // Not a folder path: a whole volume stays a plain folder at its mount point, anything else cannot be traced.
                    if (!VolumeOnly().IsMatch(stripped))
                    {
                        throw Untraceable(candidate, target);
                    }
                }
                else
                {
                    if (++hops > _maxLinkHops)
                    {
                        throw new IOException($"Too many levels of links while resolving '{path}'.");
                    }

                    if (syntax.IsAbsolute(stripped))
                    {
                        var absolute = syntax.Split(stripped, collapseDots: false);
                        root = absolute.Root;
                        resolved.Clear();
                        PushParts(pending, absolute.Parts);
                    }
                    else
                    {
                        if (isWindows && stripped.Contains(':'))
                        {
                            throw Untraceable(candidate, target);
                        }

                        // A relative target starts at the folder holding the link (what is resolved so far); a drive-less "\Users" restarts at the drive.
                        if (isWindows && stripped.Length > 0 && stripped[0] is '\\' or '/')
                        {
                            resolved.Clear();
                        }

                        PushParts(pending, syntax.SplitRelative(stripped).ToList());
                    }

                    continue;
                }
            }

            if (isWindows && part.Contains('~'))
            {
                part = probe.ExpandShortName(candidate) ?? part;
            }

            resolved.Add(part);
        }

        return syntax.Join(root, resolved);
    }

    [GeneratedRegex(@"^Volume\{[^}\\]*\}\\?$", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeOnly();

    private static void PushParts(Stack<string> pending, IReadOnlyList<string> parts)
    {
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            pending.Push(parts[i]);
        }
    }

    // .NET already drops the \??\ prefix from a junction's target; the others are stripped here so the forms compare alike.
    private static string StripNamespacePrefix(string target) =>
        target.StartsWith(@"\??\", StringComparison.Ordinal)
        || target.StartsWith(@"\\?\", StringComparison.Ordinal)
        || target.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? target[4..]
            : target;

    // Object-manager names for a volume or device, which no folder-path rule can judge.
    private static bool IsNamespaceForm(string target) =>
        target.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("GLOBALROOT", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith(@"Device\", StringComparison.OrdinalIgnoreCase);

    private static IOException Untraceable(string link, string target) =>
        new($"The link '{link}' leads to '{target}', which cannot be traced to a folder.");
}
