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
            return JsonSerializer.Deserialize<string[]>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // A recorded path is only ever created under the game folder: relative, no way out of it, and nothing named like a git directory.
    public static bool IsSafe(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\', StringComparison.Ordinal) || path.Any(char.IsControl) || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") && !segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
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
                    else if (entry is DirectoryInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
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
