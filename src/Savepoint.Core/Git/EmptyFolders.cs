using System.Text.Json;

namespace Savepoint.Core.Git;

// git does not track an empty folder, so a point could not bring one back. The folders a point was taken with travel in the point's own
// message as one trailer: a JSON array of '/'-separated paths, innermost folders only (their parents come with them). Being part of the
// commit it follows the point through removal, bringing back, pruning and a move of the storage folder with no extra bookkeeping.
internal static class EmptyFolders
{
    public const string TrailerKey = "Savepoint-Empty-Folders";

    // ASCII only (non-ASCII becomes \uXXXX), one line, so a name can neither break the trailer nor pass for another.
    public static string Encode(IReadOnlyList<string> folders) => JsonSerializer.Serialize(folders);

    // A trailer that does not parse is no list: a point never fails to switch because of it.
    public static IReadOnlyList<string> Decode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            // Only text entries count: a null or a number in the list is dropped, the rest still stand.
            return JsonSerializer.Deserialize<JsonElement[]>(value)?.Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray() ?? [];
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return [];
        }
    }

    // A recorded path is only ever created under the game folder: relative, no way out of it, and nothing named like a git directory.
    public static bool IsSafe(string path) => IsSafe(path, OperatingSystem.IsWindows());

    // Where backslash and colon are path syntax (Windows) they are refused; elsewhere they are ordinary name characters. On Windows a name
    // that ends in a dot or a space, or is a device name, is read as a different path than the one written.
    public static bool IsSafe(string path, bool windows)
    {
        if (path.Length == 0 || path[0] == '/' || path.Any(char.IsControl) || (windows && (path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal))))
        {
            return false;
        }

        return path.Split('/').All(segment =>
            segment.Length > 0
            && segment is not ("." or "..")
            && !segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            && !(windows && (segment[^1] is '.' or ' ' || IsDeviceName(segment))));
    }

    private static bool IsDeviceName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? segment : segment[..dot];
        return stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && stem[3] is >= '1' and <= '9' && stem[..3].ToUpperInvariant() is "COM" or "LPT");
    }

    // True when the folder, joined to the work tree and resolved by the system, is that very path and lies under the work tree.
    public static bool ResolvesInside(string workTree, string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workTree));
        var joined = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        var resolved = Path.GetFullPath(joined);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(resolved, joined, comparison) && resolved.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    // Folders with nothing in them at all, relative to the work tree. A folder holding only an empty folder is not listed, its inner one is.
    // Links are not followed and a folder that is a repository of its own is not looked into; neither is ours to record.
    public static IReadOnlyList<string> Find(string workTree, CancellationToken ct)
    {
        List<string> found = [];
        Stack<(string Full, string Relative)> pending = new();
        pending.Push((workTree, string.Empty));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (full, relative) = pending.Pop();
            var any = false;
            var repository = false;
            List<(string Full, string Relative)> inside = [];
            try
            {
                foreach (var entry in new DirectoryInfo(full).EnumerateFileSystemInfos())
                {
                    any = true;
                    if (entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    {
                        repository = true;
                    }
                    else if (entry is DirectoryInfo && !LinkedFolders.IsLink(entry.FullName))
                    {
                        inside.Add((entry.FullName, relative.Length == 0 ? entry.Name : relative + "/" + entry.Name));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (repository)
            {
                continue;
            }

            if (!any && relative.Length > 0)
            {
                found.Add(relative);
            }

            foreach (var child in inside)
            {
                pending.Push(child);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
