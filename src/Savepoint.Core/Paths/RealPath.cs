using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

public static class RealPath
{
    // Matches the Linux symlink limit; a longer chain is a cycle for any practical purpose.
    private const int _maxLinkHops = 40;

    /// <summary>
    /// Resolves <paramref name="path"/> against the machine the process runs on, following every symlink
    /// and junction. A relative path is taken from the current directory.
    /// </summary>
    public static string Resolve(string path)
    {
        var syntax = PathSyntax.For(PlatformFactory.CurrentOs);
        var absolute = syntax.IsAbsolute(path) ? path : Path.GetFullPath(path);
        return Resolve(absolute, syntax, PathProbes.Host);
    }

    /// <summary>
    /// Absolute, normalized, with the symlink or junction of every existing component followed. A link whose
    /// target is missing is replaced by the target's text and resolution carries on (the check then sees
    /// where a later write would land). Components that do not exist are appended unchanged.
    /// </summary>
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
                    // Relative to the folder that holds the link, which is exactly what is resolved so far.
                    // A rooted target without a drive ("\Users") restarts at the current drive.
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
