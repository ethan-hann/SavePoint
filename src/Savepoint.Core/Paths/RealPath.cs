using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

public static class RealPath
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

    /// <summary>A dangling link is replaced by its target's text so the check sees where a write would land; missing components are kept.</summary>
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
            if (target is not null && !IsVolumeMountPoint(target))
            {
                if (++hops > _maxLinkHops)
                {
                    throw new IOException($"Too many levels of links while resolving '{path}'.");
                }

                target = StripNtPrefix(target);
                if (syntax.IsAbsolute(target))
                {
                    var absolute = syntax.Split(target, collapseDots: false);
                    root = absolute.Root;
                    resolved.Clear();
                    PushParts(pending, absolute.Parts);
                }
                else
                {
                    // A relative target starts at the folder holding the link (what is resolved so far); a drive-less "\Users" restarts at the drive.
                    if (isWindows && target.Length > 0 && target[0] is '\\' or '/')
                    {
                        resolved.Clear();
                    }

                    PushParts(pending, syntax.SplitRelative(target).ToList());
                }

                continue;
            }

            if (isWindows && part.Contains('~'))
            {
                part = probe.ExpandShortName(candidate) ?? part;
            }

            resolved.Add(part);
        }

        return syntax.Join(root, resolved);
    }

    private static void PushParts(Stack<string> pending, IReadOnlyList<string> parts)
    {
        for (var i = parts.Count - 1; i >= 0; i--)
        {
            pending.Push(parts[i]);
        }
    }

    private static string StripNtPrefix(string target) =>
        target.StartsWith(@"\??\", StringComparison.Ordinal) ? target[4..] : target;

    // A volume mount point targets a volume GUID, not a folder; the mount point itself is where the user sees the data.
    private static bool IsVolumeMountPoint(string target) =>
        StripNtPrefix(target).StartsWith("Volume{", StringComparison.OrdinalIgnoreCase);
}
